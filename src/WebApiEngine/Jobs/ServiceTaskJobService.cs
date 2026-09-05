using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Jobs;

/// <summary>
/// Vergabe und Rueckmeldung von Auftraegen an externe Worker.
///
/// Die Vergabe ist in der Ablage atomar. Zusaetzlich laeuft jede Zustandsaenderung eines
/// Auftrags in diesem Prozess unter einer Sperre und liest den Auftrag dabei neu: Waere der
/// Abschluss davon ausgenommen, koennte ein Worker mit abgelaufener Frist noch abschliessen,
/// waehrend ein zweiter denselben Auftrag bereits uebernommen hat, und ein Service-Task mit
/// Seiteneffekt liefe doppelt.
/// </summary>
public sealed class ServiceTaskJobService(
    ITransactionalStorageProvider storageProvider,
    BpmnBusinessLogic businessLogic,
    TimeProvider timeProvider,
    ILogger<ServiceTaskJobService> logger)
{
    private readonly SemaphoreSlim _assignmentLock = new(1, 1);

    /// <summary>
    /// Bildet den Sperrinhaber. Er enthaelt die authentifizierte Person, nicht nur die frei
    /// gewaehlte Worker-Kennung aus dem Anfragekoerper: Sonst genuegte ein geratener Name, um
    /// die Sperre eines fremden Workers zu bedienen.
    /// </summary>
    public static string BuildLockOwner(Guid userId, string workerId) => $"{userId:N}:{workerId}";

    public async Task<IReadOnlyList<ServiceTaskJob>> FetchAndLock(
        string type,
        Guid userId,
        string workerId,
        int maxJobs,
        TimeSpan lockDuration)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lockOwner = BuildLockOwner(userId, workerId);

        await _assignmentLock.WaitAsync();
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            var claimed = await storage.ServiceTaskStorage.ClaimJobs(type, lockOwner, now, now.Add(lockDuration), maxJobs);
            storage.CommitChanges();

            if (claimed.Count > 0)
            {
                logger.LogInformation("{Count} Auftraege vom Typ {Type} an Worker {Worker} vergeben.", claimed.Count, type, workerId);
            }

            return claimed;
        }
        finally
        {
            _assignmentLock.Release();
        }
    }

    /// <summary>
    /// Nimmt das Ergebnis entgegen. Der Auftrag muss diesem Worker im Moment des Abschlusses
    /// noch gehoeren; sonst hat inzwischen ein anderer uebernommen.
    /// </summary>
    public async Task<JobOperationResult> Complete(Guid jobId, Guid userId, string workerId, Variables? variables)
    {
        var lockOwner = BuildLockOwner(userId, workerId);

        await _assignmentLock.WaitAsync();
        try
        {
            var (job, problem) = await LoadOwnJob(jobId, lockOwner);
            if (problem is not null)
            {
                return problem.Value;
            }

            await businessLogic.CompleteServiceTaskJob(job!, variables, userId);
            return JobOperationResult.Ok;
        }
        finally
        {
            _assignmentLock.Release();
        }
    }

    /// <summary>
    /// Meldet einen Fehlschlag. Bleiben Versuche uebrig, wird der Auftrag nach einer Wartezeit
    /// wieder vergeben; sonst bleibt er liegen und wartet auf einen Eingriff, statt still zu
    /// verschwinden.
    /// </summary>
    public async Task<JobOperationResult> Fail(
        Guid jobId,
        Guid userId,
        string workerId,
        string? errorMessage,
        int? remainingRetries,
        TimeSpan retryBackoff)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lockOwner = BuildLockOwner(userId, workerId);

        await _assignmentLock.WaitAsync();
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            var job = await storage.ServiceTaskStorage.GetLockedJob(jobId, lockOwner, now);
            if (job is null)
            {
                return await ClassifyMissingJob(storage, jobId);
            }

            // Der Worker darf die Zahl der Versuche senken, nicht erhoehen: Sonst koennte ein
            // fehlerhafter Worker sich selbst unbegrenzt viele Anlaeufe verschaffen.
            var proposed = remainingRetries ?? job.Retries - 1;
            job.Retries = Math.Clamp(proposed, 0, job.Retries - 1);
            job.LastErrorMessage = errorMessage;
            job.LockedBy = null;
            job.LockedUntil = null;
            job.RetryAt = job.Retries > 0 ? now.Add(retryBackoff) : null;

            await storage.ServiceTaskStorage.SaveJob(job);
            storage.CommitChanges();

            logger.LogWarning(
                "Auftrag {JobId} vom Typ {Type} gescheitert ({Retries} Versuche verbleiben): {Error}",
                job.Id, job.Type, job.Retries, errorMessage);

            return JobOperationResult.Ok;
        }
        finally
        {
            _assignmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<ServiceTaskJob>> GetAll()
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return (await storage.ServiceTaskStorage.GetJobs()).OrderBy(job => job.CreatedAt).ToList();
    }

    private async Task<(ServiceTaskJob? Job, JobOperationResult? Problem)> LoadOwnJob(Guid jobId, string lockOwner)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        using var storage = storageProvider.GetTransactionalStorage();

        var job = await storage.ServiceTaskStorage.GetLockedJob(jobId, lockOwner, now);
        if (job is not null)
        {
            return (job, null);
        }

        return (null, await ClassifyMissingJob(storage, jobId));
    }

    /// <summary>
    /// Trennt "gibt es nicht" von "gehoert gerade jemand anderem". Der Unterschied sagt dem
    /// Worker, ob er den Auftrag vergessen oder ihn erneut abholen soll.
    /// </summary>
    private async Task<JobOperationResult> ClassifyMissingJob(ITransactionalStorage storage, Guid jobId)
    {
        var existing = await storage.ServiceTaskStorage.GetJob(jobId);
        if (existing is null)
        {
            return JobOperationResult.NotFound;
        }

        return existing.LockedUntil is null || existing.LockedUntil <= timeProvider.GetUtcNow().UtcDateTime
            ? JobOperationResult.LockExpired
            : JobOperationResult.NotLockedByWorker;
    }
}

public enum JobOperationResult
{
    Ok,
    NotFound,
    NotLockedByWorker,
    LockExpired
}
