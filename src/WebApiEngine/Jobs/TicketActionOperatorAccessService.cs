using StorageSystem;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Jobs;

/// <summary>Persönlicher aktueller Betriebsnachweis, noch keine TT-Wiederfreigabe.</summary>
public sealed class TicketActionOperatorAccessService(ITransactionalStorageProvider storageProvider,
    IKeycloakSubjectAccessReader reader, FlowzerAuthenticationOptions authentication,
    TimeProvider timeProvider, TickyTaskTicketActionsOptions ticketActions)
{
    /// <summary>
    /// Prüft beide persönlichen Clientzugänge und Betriebsrolle im selben Zehnsekundenbudget.
    /// Vor und nach Provider-I/O werden unveränderliche Instanzkoordinaten ohne gehaltene Engine-Lease gelesen.
    /// Weder die ursprüngliche Person noch ein Job werden geändert; TT muss selbst frisch administrativ autorisieren.
    /// </summary>
    public async Task<TicketActionOperatorAccessOutcome> CheckAsync(Guid instanceId, CurrentUserContext actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Options und tatsächliche Akteurbindung als Werte einfrieren. Kein
        // Requestbody darf eine Person, Rolle oder Clientaudience auswählen.
        var issuer = authentication.JwtBearer.Authority;
        var client = authentication.JwtBearer.Audience;
        var accessRole = authentication.JwtBearer.RequiredRole;
        var operatorRole = authentication.JwtBearer.Roles.Operator;
        var hostClient = ticketActions.ApiClientId;
        if (!authentication.IsAuthenticationEnabled || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(accessRole) || string.IsNullOrWhiteSpace(operatorRole)
            || !TickyTaskTicketActionsOptions.IsValidApiClientId(hostClient, client))
            return Failed(TicketActionOperatorAccessStatus.Unavailable);
        var identity = actor?.Identity;
        var authorizedClient = actor?.AuthorizedClientId;
        if (instanceId == Guid.Empty || actor is null || actor.IsFallback || actor.UserId == Guid.Empty
            || identity is null || identity.Issuer != issuer || !KeycloakDirectoryOptions.IsSafeProviderId(identity.Subject)
            || authorizedClient is null || !KeycloakDirectoryOptions.IsSafeProviderId(authorizedClient))
            return Failed(TicketActionOperatorAccessStatus.InvalidContext);

        var (before, status) = await ReadContextAsync(instanceId, issuer, ct);
        if (before is null) return Failed(status);
        DateTimeOffset checkedAt;
        try
        {
            // Die inneren vorhandenen Reads dürfen das gemeinsame Budget nicht
            // erneuern. TT-Zugang des BEDIENENDEN Operators ist zusätzlich zu
            // TT-Admin/Membership nötig, nicht bloß der Flowzer-Operatorclaim.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var allowed = await reader.HasCurrentTicketActionAccessAsync(identity, client, accessRole, hostClient, linked.Token)
                && await reader.HasCurrentAccessAsync(identity, client, operatorRole, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!allowed) return Failed(TicketActionOperatorAccessStatus.Denied);
            checkedAt = timeProvider.GetUtcNow(); // Tatsächlicher Rollenprüfzeitpunkt, nicht ein späterer Storage-/Antwortzeitpunkt.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (OperationCanceledException) { return Failed(TicketActionOperatorAccessStatus.Unavailable); }
        catch (KeycloakAdminClientException) { return Failed(TicketActionOperatorAccessStatus.Unavailable); }

        ct.ThrowIfCancellationRequested();
        var (after, afterStatus) = await ReadContextAsync(instanceId, issuer, ct);
        if (after is null) return Failed(afterStatus);
        if (before != after) return Failed(TicketActionOperatorAccessStatus.InvalidContext);
        return new(TicketActionOperatorAccessStatus.Ok, new(after.InstanceId, after.MetaDefinitionId,
            after.DefinitionId, after.Initiator.Issuer, after.Initiator.Subject,
            identity.Issuer, identity.Subject, authorizedClient, checkedAt));
    }

    private async Task<(InstanceContext? Context, TicketActionOperatorAccessStatus Status)> ReadContextAsync(Guid instanceId,
        string issuer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var storage = storageProvider.GetTransactionalStorage();
        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return (null, TicketActionOperatorAccessStatus.NotFound); }
        var masters = instance.Tokens.Where(token => token.ParentTokenId is null).Take(2).ToArray();
        // Der persistierte äußere Vorgang wird weiterhin an die angefragte ID gebunden.
        // Der getrennt erzeugte interne Scope ist nur gültig, wenn alle gespeicherten
        // Token dem einen nichtleeren Master-Scope angehören; kein fremder Scope wird geraten.
        if (instance.InstanceId != instanceId || instance.IsFinished
            || instance.State is not (ProcessInstanceState.Running or ProcessInstanceState.Waiting)
            || instance.DefinitionId == Guid.Empty || string.IsNullOrWhiteSpace(instance.metaDefinitionId)
            || instance.metaDefinitionId.Length > 256 || instance.metaDefinitionId.Any(char.IsControl)
            || masters.Length != 1 || masters[0].Id == Guid.Empty || masters[0].ProcessInstanceId == Guid.Empty
            || instance.Tokens.Any(token => token.ProcessInstanceId != masters[0].ProcessInstanceId)
            || masters[0].Initiator is not { } initiator || initiator.Issuer != issuer
            || !KeycloakDirectoryOptions.IsSafeProviderId(initiator.Subject))
            return (null, TicketActionOperatorAccessStatus.InvalidContext);
        ct.ThrowIfCancellationRequested();
        // Nicht die mutable Storage-/Tokenreferenz behalten: ein In-place-Wechsel
        // von Version/Initiator während Provider-I/O muss den Proof entwerten.
        return (new(instance.InstanceId, instance.metaDefinitionId, instance.DefinitionId,
            instance.ProcessId, instance.State, masters[0].Id, masters[0].ProcessInstanceId, initiator), TicketActionOperatorAccessStatus.Ok);
    }

    private static TicketActionOperatorAccessOutcome Failed(TicketActionOperatorAccessStatus status) => new(status, null);
    private sealed record InstanceContext(Guid InstanceId, string MetaDefinitionId, Guid DefinitionId,
        string ProcessId, ProcessInstanceState State, Guid MasterTokenId, Guid InternalScopeId, AuthenticatedSubject Initiator);
}

/// <summary>Technische Unklarheit, Entzug und Kontextkonflikt sind getrennt; nur Ok enthält einen aktuellen Stand.</summary>
public enum TicketActionOperatorAccessStatus { Ok, NotFound, Denied, InvalidContext, Unavailable }

/// <summary>Der Nachweis lässt alle Engine-/TT-Mutationen ausdrücklich beim aufrufenden autorisierten Use-Case.</summary>
public sealed record TicketActionOperatorAccessOutcome(TicketActionOperatorAccessStatus Status, TicketActionOperatorAccessDto? Access);
