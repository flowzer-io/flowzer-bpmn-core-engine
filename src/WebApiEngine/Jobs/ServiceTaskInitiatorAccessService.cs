using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Jobs;

/// <summary>Jobgebundener Live-Zugang des Initiators; weder Ticketwirkung noch Jobmutation.</summary>
public sealed class ServiceTaskInitiatorAccessService(ITransactionalStorageProvider storageProvider,
    IKeycloakSubjectAccessReader reader, FlowzerAuthenticationOptions authentication, TimeProvider timeProvider)
{
    /// <summary>
    /// Prüft eigenen laufenden Auftrag vor und nach der externen Zugangsabfrage. Kein
    /// Engine-/SQL-Lock bleibt während Keycloak-I/O offen; Umbinden/Abbruch entwertet das Ja.
    /// Der Verbraucher muss zusätzlich seine eigenen TT-Vorgangs- und Ticketrechte prüfen.
    /// </summary>
    public async Task<ServiceTaskInitiatorAccessOutcome> CheckAsync(Guid jobId, Guid workerUserId, string workerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var auth = authentication;
        if (!auth.IsAuthenticationEnabled || string.IsNullOrWhiteSpace(auth.JwtBearer.RequiredRole)
            || string.IsNullOrWhiteSpace(auth.JwtBearer.Audience) || string.IsNullOrWhiteSpace(auth.JwtBearer.Authority))
            return Failed(ServiceTaskInitiatorAccessStatus.Unavailable);
        if (jobId == Guid.Empty || workerUserId == Guid.Empty || !IsValidWorkerId(workerId))
            return Failed(ServiceTaskInitiatorAccessStatus.InvalidContext);
        var owner = ServiceTaskJobService.BuildLockOwner(workerUserId, workerId);
        var (before, status) = await ReadContextAsync(jobId, owner, auth.JwtBearer.Authority, cancellationToken);
        if (before is null) return Failed(status);
        bool allowed;
        try
        {
            allowed = await reader.HasCurrentAccessAsync(before.Identity, auth.JwtBearer.Audience,
                auth.JwtBearer.RequiredRole, cancellationToken);
        }
        catch (KeycloakAdminClientException) { return Failed(ServiceTaskInitiatorAccessStatus.Unavailable); }
        cancellationToken.ThrowIfCancellationRequested();
        var (after, afterStatus) = await ReadContextAsync(jobId, owner, auth.JwtBearer.Authority, cancellationToken);
        if (after is null) return Failed(afterStatus);
        if (after != before) return Failed(ServiceTaskInitiatorAccessStatus.InvalidContext);
        return new(ServiceTaskInitiatorAccessStatus.Ok, new(
            after.JobId, after.InstanceId, after.MetaDefinitionId, after.DefinitionId, after.TokenId,
            after.FlowNodeId, after.Type, after.Identity.Issuer, after.Identity.Subject, allowed, timeProvider.GetUtcNow()));
    }

    /// <summary>Die TT-Worker-Nonce ist ein kurzer technischer Schlüssel, kein Anzeigename.</summary>
    public static bool IsValidWorkerId(string? workerId) => workerId is { Length: > 0 and <= 128 }
        && workerId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private async Task<(ExecutionContext? Context, ServiceTaskInitiatorAccessStatus Status)> ReadContextAsync(
        Guid jobId, string owner, string issuer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var storage = storageProvider.GetTransactionalStorage();
        var job = await storage.ServiceTaskStorage.GetLockedJob(jobId, owner, timeProvider.GetUtcNow().UtcDateTime);
        if (job is null) return (null, await storage.ServiceTaskStorage.GetJob(jobId) is null
            ? ServiceTaskInitiatorAccessStatus.NotFound : ServiceTaskInitiatorAccessStatus.LeaseLost);
        if (job.Type is not ("tt.ticket.read" or "tt.ticket.create" or "tt.ticket.close" or "tt.ticket.delegate")
            || job.Id != jobId || job.ProcessInstanceId == Guid.Empty || job.DefinitionId == Guid.Empty || job.TokenId == Guid.Empty)
            return (null, ServiceTaskInitiatorAccessStatus.InvalidContext);
        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(job.ProcessInstanceId); }
        catch (FileNotFoundException) { return (null, ServiceTaskInitiatorAccessStatus.InvalidContext); }
        var masters = instance.Tokens.Where(token => token.ParentTokenId is null).Take(2).ToArray();
        var waitingTokens = instance.Tokens.Where(token => token.Id == job.TokenId).Take(2).ToArray();
        if (instance.InstanceId != job.ProcessInstanceId || instance.IsFinished
            || instance.State is not (ProcessInstanceState.Running or ProcessInstanceState.Waiting)
            || instance.DefinitionId != job.DefinitionId || instance.metaDefinitionId != job.MetaDefinitionId || instance.ProcessId != job.ProcessId
            || masters.Length != 1 || masters[0].ProcessInstanceId != instance.InstanceId
            || masters[0].Initiator is not { } identity || identity.Issuer != issuer || string.IsNullOrWhiteSpace(identity.Subject)
            || waitingTokens.Length != 1 || waitingTokens[0].ProcessInstanceId != instance.InstanceId
            || waitingTokens[0].CurrentBaseElement is not BPMN.Activities.ServiceTask
            || waitingTokens[0].CurrentBaseElement.Id != job.FlowNodeId || waitingTokens[0].State != FlowNodeState.Active)
            return (null, ServiceTaskInitiatorAccessStatus.InvalidContext);
        cancellationToken.ThrowIfCancellationRequested();
        if (job.LockedUntil is null || job.LockedUntil <= timeProvider.GetUtcNow().UtcDateTime)
            return (null, ServiceTaskInitiatorAccessStatus.LeaseLost);
        // Immutable Kopie: Memory-/Dateiadapter können dieselbe mutable Job-/Instanzreferenz
        // erneut liefern. Ein In-place-Umbinden darf den Vorherstand nicht mit verändern.
        return (new(job.Id, job.ProcessInstanceId, job.MetaDefinitionId, job.DefinitionId, job.TokenId,
            job.FlowNodeId, job.Type, job.ProcessId, identity), ServiceTaskInitiatorAccessStatus.Ok);
    }

    private static ServiceTaskInitiatorAccessOutcome Failed(ServiceTaskInitiatorAccessStatus status) => new(status, null);
    private sealed record ExecutionContext(Guid JobId, Guid InstanceId, string MetaDefinitionId,
        Guid DefinitionId, Guid TokenId, string FlowNodeId, string Type, string ProcessId, AuthenticatedSubject Identity);
}

/// <summary>Closed-world Ergebnis; technische Unklarheit wird nicht zu bestätigtem Entzug.</summary>
public enum ServiceTaskInitiatorAccessStatus { Ok, NotFound, LeaseLost, InvalidContext, Unavailable }

/// <summary>Nur Ok enthält einen gebundenen, minimalen Zugangsstand.</summary>
public sealed record ServiceTaskInitiatorAccessOutcome(ServiceTaskInitiatorAccessStatus Status, ServiceTaskInitiatorAccessDto? Access);
