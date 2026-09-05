using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Jobs;

/// <summary>
/// Vergabe und Rueckmeldung von Auftraegen an externe Worker.
///
/// Die Vergabe laeuft unter einer eigenen Sperre. Ohne sie koennten zwei Worker denselben
/// Auftrag lesen, beide die Sperre setzen und beide die Arbeit machen; bei einem Service-Task
/// mit Seiteneffekt waere das eine doppelte Ausfuehrung.
/// </summary>
public sealed class ServiceTaskJobService(
    ITransactionalStorageProvider storageProvider,
    BpmnBusinessLogic businessLogic,
    TimeProvider timeProvider,
    ILogger<ServiceTaskJobService> logger)
{
    private readonly SemaphoreSlim _assignmentLock = new(1, 1);

    public async Task<IReadOnlyList<ServiceTaskJob>> FetchAndLock(string type, string workerId, int maxJobs, TimeSpan lockDuration)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await _assignmentLock.WaitAsync();
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();

            var available = (await storage.ServiceTaskStorage.GetJobsByType(type))
                .Where(job => job.IsAvailableAt(now))
                .OrderBy(job => job.CreatedAt)
                .Take(maxJobs)
                .ToList();

            foreach (var job in available)
            {
                job.LockedBy = workerId;
                job.LockedUntil = now.Add(lockDuration);
                await storage.ServiceTaskStorage.SaveJob(job);
            }

            storage.CommitChanges();

            if (available.Count > 0)
            {
                logger.LogInformation("{Count} Auftraege vom Typ {Type} an Worker {Worker} vergeben.", available.Count, type, workerId);
            }

            return available;
        }
        finally
        {
            _assignmentLock.Release();
        }
    }

    /// <summary>
    /// Nimmt das Ergebnis entgegen. Der Auftrag muss diesem Worker gehoeren und seine Frist
    /// darf nicht abgelaufen sein: Sonst hat inzwischen ein anderer Worker uebernommen, und
    /// zwei Ergebnisse fuer denselben Token wuerden den Prozess doppelt weiterfuehren.
    /// </summary>
    public async Task<JobOperationResult> Complete(Guid jobId, string workerId, Variables? variables, Guid userId)
    {
        var job = await LoadJob(jobId);
        if (job is null)
        {
            return JobOperationResult.NotFound;
        }

        var lockProblem = CheckLock(job, workerId);
        if (lockProblem is not null)
        {
            return lockProblem.Value;
        }

        await businessLogic.CompleteServiceTaskJob(job, variables, userId);
        return JobOperationResult.Ok;
    }

    /// <summary>
    /// Meldet einen Fehlschlag. Bleiben Versuche uebrig, wird der Auftrag nach einer Wartezeit
    /// wieder vergeben; sonst bleibt er liegen und wartet auf einen Eingriff, statt still zu
    /// verschwinden.
    /// </summary>
    public async Task<JobOperationResult> Fail(Guid jobId, string workerId, string? errorMessage, int? remainingRetries, TimeSpan retryBackoff)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await _assignmentLock.WaitAsync();
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            var job = await storage.ServiceTaskStorage.GetJob(jobId);
            if (job is null)
            {
                return JobOperationResult.NotFound;
            }

            var lockProblem = CheckLock(job, workerId);
            if (lockProblem is not null)
            {
                return lockProblem.Value;
            }

            job.Retries = remainingRetries ?? Math.Max(0, job.Retries - 1);
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

    private async Task<ServiceTaskJob?> LoadJob(Guid jobId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return await storage.ServiceTaskStorage.GetJob(jobId);
    }

    private JobOperationResult? CheckLock(ServiceTaskJob job, string workerId)
    {
        if (!string.Equals(job.LockedBy, workerId, StringComparison.Ordinal))
        {
            return JobOperationResult.NotLockedByWorker;
        }

        if (job.LockedUntil is null || job.LockedUntil <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return JobOperationResult.LockExpired;
        }

        return null;
    }
}

public enum JobOperationResult
{
    Ok,
    NotFound,
    NotLockedByWorker,
    LockExpired
}
