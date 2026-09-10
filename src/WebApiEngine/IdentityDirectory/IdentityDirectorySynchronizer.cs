using Microsoft.Extensions.Options;
using StorageSystem;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Führt einen vollständigen Keycloak-Import lokal nur einmal gleichzeitig aus und publiziert
/// ausschließlich den komplett gelesenen Stand. Ein fehlerhafter Lauf verändert den aktiven
/// Snapshot nicht.
/// </summary>
public sealed class IdentityDirectorySynchronizer(
    IKeycloakAdminClient client,
    IIdentityDirectoryStorage storage,
    IOptions<KeycloakDirectoryOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityDirectorySynchronizer> logger)
{
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly KeycloakDirectoryOptions _options = options.Value;

    public enum SynchronizationOutcome
    {
        Disabled,
        Busy,
        Succeeded,
        Failed
    }

    /// <summary>
    /// Versucht genau einen Abgleich. Ein bereits laufender lokaler Import gewinnt; weitere
    /// Auslöser kehren ohne Warteschlange mit <c>false</c> zurück.
    /// </summary>
    public async Task<bool> TrySynchronizeAsync(CancellationToken cancellationToken)
        => await SynchronizeAsync(cancellationToken) == SynchronizationOutcome.Succeeded;

    /// <summary>Fuehrt einen Abgleich aus und liefert fuer administrative Ausloeser ein eindeutiges Ergebnis.</summary>
    public async Task<SynchronizationOutcome> SynchronizeAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return SynchronizationOutcome.Disabled;
        if (!_singleFlight.Wait(0)) return SynchronizationOutcome.Busy;

        var started = false;
        string? issuer = null;
        var generationId = Guid.Empty;
        try
        {
            issuer = BuildIssuer();
            generationId = Guid.NewGuid();
            var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            var synchronizationTimeout = TimeSpan.FromSeconds(Math.Clamp(_options.SynchronizationTimeoutSeconds, 10, 3_600));
            var leaseExpiresAtUtc = startedAtUtc
                .Add(synchronizationTimeout)
                .AddSeconds(Math.Clamp(_options.LeaseGraceSeconds, 1, 600));
            if (!await storage.TryStartSync(issuer, generationId, startedAtUtc, leaseExpiresAtUtc))
            {
                return SynchronizationOutcome.Busy;
            }
            started = true;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(synchronizationTimeout);
            var keycloakSnapshot = await client.GetSnapshotAsync(timeout.Token);
            var snapshot = MapSnapshot(keycloakSnapshot, issuer, generationId, timeProvider.GetUtcNow().UtcDateTime);
            await storage.PublishSnapshot(snapshot);
            logger.LogInformation("Keycloak directory snapshot published with {UserCount} users and {GroupCount} groups.",
                snapshot.Users.Count, snapshot.Groups.Count);
            return SynchronizationOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (started) await RecordFailureAsync(issuer!, generationId, "cancelled");
            // Beim geregelten Host-Shutdown bleibt der letzte vollständige Snapshot aktiv, der
            // angefangene Lauf wird aber sichtbar beendet und blockiert keinen Neustart.
            throw;
        }
        catch (OperationCanceledException)
        {
            if (started) await RecordFailureAsync(issuer!, generationId, "timeout");
            logger.LogWarning("Keycloak directory synchronization exceeded its configured time limit.");
            return SynchronizationOutcome.Failed;
        }
        catch (KeycloakAdminClientException exception)
        {
            if (started) await RecordFailureAsync(issuer!, generationId, ToErrorCode(exception.Kind));
            logger.LogWarning("Keycloak directory synchronization failed with classified error {ErrorCode}.", ToErrorCode(exception.Kind));
            return SynchronizationOutcome.Failed;
        }
        catch (Exception)
        {
            if (started) await RecordFailureAsync(issuer!, generationId, "unexpected");
            // Kein Exception-Objekt loggen: Providerfehler dürfen weder Antwortkörper noch Secrets
            // indirekt in die Logs befördern.
            logger.LogError("Keycloak directory synchronization failed with an unexpected error.");
            return SynchronizationOutcome.Failed;
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    private async Task RecordFailureAsync(string issuer, Guid generationId, string errorCode)
    {
        try
        {
            var recorded = await storage.FailSync(
                issuer,
                generationId,
                errorCode,
                "Keycloak directory synchronization failed.",
                timeProvider.GetUtcNow().UtcDateTime);
            if (!recorded)
            {
                logger.LogWarning("A stale Keycloak directory failure was ignored because a newer synchronization owns the lease.");
            }
        }
        catch (Exception)
        {
            // Der aktive Snapshot bleibt unverändert. Auch der Speicherfehler wird absichtlich
            // nicht inklusive Exception geloggt, um keine unkontrollierten Providerdaten zu zeigen.
            logger.LogError("Keycloak directory synchronization failure status could not be persisted.");
        }
    }

    private string BuildIssuer()
    {
        if (!Uri.TryCreate(_options.Issuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(issuer.UserInfo)
            || !string.IsNullOrEmpty(issuer.Query)
            || !string.IsNullOrEmpty(issuer.Fragment))
        {
            throw new KeycloakAdminClientException(
                KeycloakAdminClientFailureKind.Configuration,
                "Keycloak directory synchronization is not configured with a valid HTTPS issuer.");
        }

        // Der OIDC-Issuer ist ein exakter Identifikator. URI-Parsing dient nur der
        // Sicherheitsvalidierung; der konfigurierte Wert darf nicht normalisiert werden.
        return _options.Issuer;
    }

    private static DirectorySnapshot MapSnapshot(
        KeycloakDirectorySnapshot source,
        string issuer,
        Guid generationId,
        DateTime completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        var groupIds = source.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var groupsByExternalId = new Dictionary<string, DirectoryGroup>(StringComparer.Ordinal);
        foreach (var group in source.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name) || string.IsNullOrWhiteSpace(group.Path))
            {
                throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a group without stable directory fields.");
            }

            if (!groupsByExternalId.TryAdd(group.Id, new DirectoryGroup
                {
                    Id = Guid.NewGuid(),
                    SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = issuer,
                    ExternalId = group.Id,
                    Name = group.Name,
                    Path = group.Path,
                    IsActive = true
                }))
            {
                throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a group identifier more than once.");
            }
        }

        foreach (var group in source.Groups.Where(group => group.ParentId is not null))
        {
            if (!groupIds.Contains(group.ParentId!))
            {
                throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a group parent outside the directory snapshot.");
            }

            groupsByExternalId[group.Id].ParentId = groupsByExternalId[group.ParentId!].Id;
        }

        var usersBySubject = new Dictionary<string, DirectoryUser>(StringComparer.Ordinal);
        var memberships = new List<DirectoryMembership>();
        foreach (var user in source.Users)
        {
            if (string.IsNullOrWhiteSpace(user.Subject))
            {
                throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a user without a stable subject.");
            }

            var mappedUser = new DirectoryUser
            {
                Id = Guid.NewGuid(),
                SourceKind = DirectorySourceKind.Keycloak,
                Issuer = issuer,
                Subject = user.Subject,
                DisplayName = BuildDisplayName(user),
                IsActive = user.Enabled
            };
            if (!usersBySubject.TryAdd(user.Subject, mappedUser))
            {
                throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a user subject more than once.");
            }

            foreach (var groupId in user.Groups.Distinct(StringComparer.Ordinal))
            {
                if (!groupsByExternalId.TryGetValue(groupId, out var mappedGroup))
                {
                    throw new KeycloakAdminClientException(KeycloakAdminClientFailureKind.InvalidResponse,
                        "Keycloak returned a membership outside the directory snapshot.");
                }

                memberships.Add(new DirectoryMembership { UserId = mappedUser.Id, GroupId = mappedGroup.Id });
            }
        }

        return new DirectorySnapshot
        {
            GenerationId = generationId,
            Issuer = issuer,
            CompletedAtUtc = completedAtUtc.ToUniversalTime(),
            Users = usersBySubject.Values.OrderBy(user => user.Subject, StringComparer.Ordinal).ToList(),
            Groups = groupsByExternalId.Values.OrderBy(group => group.Path, StringComparer.Ordinal).ToList(),
            Memberships = memberships.OrderBy(membership => membership.UserId).ThenBy(membership => membership.GroupId).ToList()
        };
    }

    private static string BuildDisplayName(KeycloakDirectoryUser user)
    {
        var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : !string.IsNullOrWhiteSpace(user.Username)
                ? user.Username
                : user.Subject;
    }

    private static string ToErrorCode(KeycloakAdminClientFailureKind kind) => kind switch
    {
        KeycloakAdminClientFailureKind.Configuration => "configuration",
        KeycloakAdminClientFailureKind.Authentication => "authentication",
        KeycloakAdminClientFailureKind.Authorization => "authorization",
        KeycloakAdminClientFailureKind.NotFound => "not_found",
        KeycloakAdminClientFailureKind.Transient => "transient",
        KeycloakAdminClientFailureKind.InvalidResponse => "invalid_response",
        _ => "permanent"
    };
}
