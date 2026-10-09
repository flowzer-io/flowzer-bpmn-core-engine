using Model;
using System.Text.Json;

namespace WebApiEngine.IdentityDirectory;

public sealed partial class KeycloakAdminClient : IKeycloakSubjectAccessReader
{
    /// <inheritdoc />
    public async Task<bool> HasCurrentAccessAsync(AuthenticatedSubject identity, string clientId, string requiredRole,
        CancellationToken cancellationToken)
    {
        // Der realmweite Legacy-Abgleich ist hier ausdrücklich kein Zugangsbeweis.
        // Opaque/federierte Subjects bleiben unverändert; nur eindeutige URL-Segmente zulassen.
        if (!_options.Enabled || !_options.IsValid() || string.IsNullOrEmpty(_options.RootGroupId)
            || identity is null || !string.Equals(identity.Issuer, _options.Issuer, StringComparison.Ordinal)
            || !KeycloakDirectoryOptions.IsSafeProviderId(identity.Subject)
            || string.IsNullOrEmpty(clientId) || clientId.Length > 256 || !KeycloakDirectoryOptions.IsSafeProviderId(clientId)
            || string.IsNullOrWhiteSpace(requiredRole) || requiredRole.Length > 256 || requiredRole.Any(char.IsControl))
            throw AccessFailure(KeycloakAdminClientFailureKind.Configuration);

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            var configuration = ValidateConfiguration();
            // Frisches Token je Prüfung, kein Zugangs-/Directory-/Rollencache. Auch Token-
            // und Gruppenantworten müssen eindeutiges JSON sein, nicht nur das Profil.
            var token = new AccessTokenLease(this, configuration, strictResponse: true);
            return await ReadSubjectAccessAsync(identity.Subject, clientId, requiredRole, configuration, token, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Keine ursprüngliche Transportexception samt URI/Antwort/Secret durchreichen.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) { throw AccessFailure(KeycloakAdminClientFailureKind.Transient); }
        catch (KeycloakAdminClientException error) { throw AccessFailure(error.Kind); }
    }

    private async Task<bool> ReadSubjectAccessAsync(string subject, string clientId, string requiredRole,
        ValidatedConfiguration configuration, AccessTokenLease token, CancellationToken cancellationToken)
    {
        var userPath = $"users/{Uri.EscapeDataString(subject)}";
        KeycloakUserRepresentation user;
        try { user = await GetResourceAsync<KeycloakUserRepresentation>(configuration.AdminResourceEndpoint(userPath), token, cancellationToken); }
        catch (KeycloakAdminClientException error) when (error.Kind == KeycloakAdminClientFailureKind.NotFound)
        {
            // Nur 404 am exakten Einzelprofil bestätigt ein gelöschtes Konto. Fehlende
            // Wurzel/Client/Rollenroute sind dagegen kein nachgewiesener persönlicher Entzug.
            return false;
        }
        if (!string.Equals(user.Id, subject, StringComparison.Ordinal) || user.Enabled is null)
            throw InvalidResponse("Keycloak returned an invalid requested user.");
        if (user.Enabled != true) return false;

        var root = await GetResourceAsync<KeycloakGroupRepresentation>(
            configuration.AdminResourceEndpoint($"groups/{Uri.EscapeDataString(_options.RootGroupId)}"), token, cancellationToken);
        if (!string.Equals(root.Id, _options.RootGroupId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(root.Path))
            throw InvalidResponse("Keycloak returned an invalid configured root.");
        var groups = new Dictionary<string, KeycloakDirectoryGroup>(StringComparer.Ordinal);
        await LoadGroupTreeAsync(root, groups, new HashSet<string>(StringComparer.Ordinal), configuration, token, cancellationToken);
        var memberships = await GetPagedAsync<KeycloakGroupRepresentation>(configuration.AdminEndpoint(userPath + "/groups"),
            group => group.Id, "membership group", token, cancellationToken);
        var hasMembership = false;
        foreach (var membership in memberships)
        {
            var id = RequireId(membership.Id, "membership group");
            if (groups.TryGetValue(id, out var loaded))
            {
                if (!string.Equals(membership.Path, loaded.Path, StringComparison.Ordinal))
                    throw InvalidResponse("Keycloak returned a changed membership.");
                hasMembership = true;
            }
            else if (string.IsNullOrWhiteSpace(membership.Path) || string.Equals(membership.Path, root.Path, StringComparison.Ordinal)
                || membership.Path.StartsWith(root.Path + "/", StringComparison.Ordinal))
                throw InvalidResponse("Keycloak returned an unknown scoped membership.");
        }
        if (!hasMembership) return false;

        // Keycloak erwartet hier seine interne Client-ID, NICHT den Audience-Text.
        // Exakte Suche mit maximal zwei Ergebnissen: Mehrdeutigkeit ist kein Fallback.
        var clients = await GetResourceAsync<List<AccessClientRepresentation>>(
            configuration.AdminResourceEndpoint($"clients?clientId={Uri.EscapeDataString(clientId)}&first=0&max=2"), token, cancellationToken);
        if (clients.Count != 1 || clients[0] is not { } client || client.ClientId != clientId || client.Enabled is null)
            throw InvalidResponse("Keycloak returned an ambiguous access client.");
        var internalClientId = RequireId(client.Id, "access client");
        if (client.Enabled != true) return false;
        var roles = await GetResourceAsync<List<AccessRoleRepresentation>>(
            configuration.AdminResourceEndpoint(userPath + $"/role-mappings/clients/{Uri.EscapeDataString(internalClientId)}/composite"), token, cancellationToken);
        var ids = new HashSet<string>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (role is null || !ids.Add(RequireId(role.Id, "access role")) || string.IsNullOrWhiteSpace(role.Name)
                || !names.Add(role.Name) || role.ClientRole != true || role.ContainerId != internalClientId)
                throw InvalidResponse("Keycloak returned an invalid effective client role.");
        }
        return names.Contains(requiredRole);
    }

    private static KeycloakAdminClientException AccessFailure(KeycloakAdminClientFailureKind kind) =>
        new(kind, "The current Keycloak access could not be established safely.");

    /// <summary>Doppelte Schlüssel bleiben auch in unbekannten Unterobjekten geschlossen.</summary>
    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                // Web-Deserialisierung ist case-insensitive; Enabled/enabled wäre ebenfalls
                // eine mehrdeutige Autoritätsquelle, selbst bei verschiedenen JSON-Schlüsseln.
                if (!names.Add(property.Name)) throw InvalidResponse("Keycloak returned ambiguous access JSON.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireUniqueProperties(item);
    }

    private sealed class AccessClientRepresentation
    {
        public string? Id { get; init; }
        public string? ClientId { get; init; }
        public bool? Enabled { get; init; }
    }
    private sealed class AccessRoleRepresentation
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public bool? ClientRole { get; init; }
        public string? ContainerId { get; init; }
    }
}
