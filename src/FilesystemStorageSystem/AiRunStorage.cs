using Model;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Einzelprozess-Entwicklungsablage fuer KI-Läufe. Ein prozessweites Gate macht Claim,
/// Revision und Lease innerhalb eines Prozesses atomar; Mehrprozessbetrieb erfordert PostgreSQL.
/// </summary>
internal sealed class AiRunStorage(Storage storage) : IAiRunStorage
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", "AiRuns"));

    public async Task<AiRunWriteResult> TryCreate(AiRun run)
    {
        AiRunStorageRules.ValidateNew(run);
        await Gate.WaitAsync();
        try
        {
            var all = ReadAll();
            var current = all.FirstOrDefault(candidate => candidate.Id == run.Id
                                                          || candidate.ProcessInstanceId == run.ProcessInstanceId
                                                          && candidate.TokenId == run.TokenId);
            if (current is not null) return Conflict(current);
            try
            {
                await StorageFile.WriteAllTextNewAtomicAsync(File(run.Id), SafeStorageJson.Serialize(run));
                return Written(run);
            }
            catch (IOException)
            {
                current = await Get(run.Id);
                return current is null
                    ? new AiRunWriteResult(AiRunWriteStatus.Conflict, null, 0)
                    : Conflict(current);
            }
        }
        finally { Gate.Release(); }
    }

    public Task<AiRun?> Get(Guid id)
    {
        ValidateId(id);
        var content = StorageFile.ReadAllTextIfExists(File(id));
        return Task.FromResult(content is null ? null : Deserialize(content));
    }

    public Task<IReadOnlyList<AiRun>> List() =>
        Task.FromResult<IReadOnlyList<AiRun>>(ReadAll()
            .OrderBy(run => run.CreatedAtUtc)
            .ThenBy(run => run.Id)
            .ToArray());

    public Task<IReadOnlyList<AiRun>> ClaimProviderRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns) => Claim(
        leaseOwner,
        nowUtc,
        leaseExpiresAtUtc,
        maxRuns,
        run => run.Status == AiRunStatus.Pending
               || run.Status == AiRunStatus.RetryScheduled && run.NextAttemptAtUtc <= nowUtc,
        AiRunStatus.Running);

    public Task<IReadOnlyList<AiRun>> ClaimResultRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns) => Claim(
        leaseOwner,
        nowUtc,
        leaseExpiresAtUtc,
        maxRuns,
        run => run.Status == AiRunStatus.ResultReady
               && run.LeaseOwner is null
               && run.LeaseExpiresAtUtc is null,
        AiRunStatus.Completing);

    public async Task<AiRun?> RenewLease(
        Guid id,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc)
    {
        ValidateId(id);
        AiRunStorageRules.ValidateLeaseArguments(leaseOwner, nowUtc, leaseExpiresAtUtc);
        await Gate.WaitAsync();
        try
        {
            var current = await Get(id);
            if (current is null
                || current.Revision != expectedRevision
                || current.Status is not (AiRunStatus.Running or AiRunStatus.Completing)
                || !string.Equals(current.LeaseOwner, leaseOwner, StringComparison.Ordinal)
                || current.LeaseExpiresAtUtc is null
                || current.LeaseExpiresAtUtc <= nowUtc)
                return null;
            if (current.LeaseExpiresAtUtc >= leaseExpiresAtUtc) return current;

            var renewed = current with
            {
                LeaseExpiresAtUtc = leaseExpiresAtUtc,
                Revision = current.Revision + 1,
                UpdatedAtUtc = nowUtc
            };
            await Save(renewed);
            return renewed;
        }
        finally { Gate.Release(); }
    }

    public async Task<AiRunWriteResult> TryUpdate(
        AiRun updated,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ValidateId(updated.Id);
        await Gate.WaitAsync();
        try
        {
            var current = await Get(updated.Id);
            if (current is null) return new AiRunWriteResult(AiRunWriteStatus.NotFound, null, 0);
            if (current.Revision != expectedRevision) return Conflict(current);
            if (!string.Equals(current.LeaseOwner, leaseOwner, StringComparison.Ordinal)
                || current.LeaseExpiresAtUtc is null
                || current.LeaseExpiresAtUtc <= nowUtc)
                return new AiRunWriteResult(AiRunWriteStatus.LeaseLost, current, current.Revision);

            AiRunStorageRules.ValidateTransition(current, updated, expectedRevision, leaseOwner, nowUtc);
            await Save(updated);
            return Written(updated);
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<AiRun>> RecoverExpiredLeases(DateTime nowUtc, int maxRuns)
    {
        EnsureUtc(nowUtc);
        if (maxRuns is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxRuns));
        await Gate.WaitAsync();
        try
        {
            var recovered = new List<AiRun>();
            foreach (var current in ReadAll()
                         .Where(run => run.LeaseExpiresAtUtc <= nowUtc
                                       && run.Status is AiRunStatus.Running or AiRunStatus.Completing)
                         .OrderBy(run => run.LeaseExpiresAtUtc)
                         .ThenBy(run => run.Id)
                         .Take(maxRuns))
            {
                var updated = current.Status switch
                {
                    AiRunStatus.Running when current.ProviderCallStartedAtUtc is null => current with
                    {
                        Status = AiRunStatus.Pending,
                        LeaseOwner = null,
                        LeaseExpiresAtUtc = null,
                        FailureCode = null,
                        Revision = current.Revision + 1,
                        UpdatedAtUtc = nowUtc
                    },
                    AiRunStatus.Running => current with
                    {
                        Status = AiRunStatus.Incident,
                        LeaseOwner = null,
                        LeaseExpiresAtUtc = null,
                        FailureCode = "ai.run.outcome_unknown",
                        Revision = current.Revision + 1,
                        UpdatedAtUtc = nowUtc
                    },
                    _ => current with
                    {
                        Status = AiRunStatus.Incident,
                        LeaseOwner = null,
                        LeaseExpiresAtUtc = null,
                        FailureCode = "ai.run.engine_outcome_unknown",
                        Revision = current.Revision + 1,
                        UpdatedAtUtc = nowUtc
                    }
                };
                await Save(updated);
                recovered.Add(updated);
            }
            return recovered;
        }
        finally { Gate.Release(); }
    }

    private async Task<IReadOnlyList<AiRun>> Claim(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns,
        Func<AiRun, bool> available,
        AiRunStatus claimedStatus)
    {
        AiRunStorageRules.ValidateLeaseArguments(leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
        await Gate.WaitAsync();
        try
        {
            var claimed = ReadAll()
                .Where(run => run.LeaseOwner is null
                              && run.LeaseExpiresAtUtc is null
                              && available(run)
                              && (claimedStatus != AiRunStatus.Running
                                  || run.Attempt < run.MaximumAttempts))
                .OrderBy(run => run.NextAttemptAtUtc ?? run.CreatedAtUtc)
                .ThenBy(run => run.Id)
                .Take(maxRuns)
                .Select(run => run with
                {
                    Status = claimedStatus,
                    LeaseOwner = leaseOwner,
                    LeaseExpiresAtUtc = leaseExpiresAtUtc,
                    ProviderCallStartedAtUtc = claimedStatus == AiRunStatus.Running
                        ? null
                        : run.ProviderCallStartedAtUtc,
                    NextAttemptAtUtc = null,
                    FailureCode = claimedStatus == AiRunStatus.Running ? null : run.FailureCode,
                    Revision = run.Revision + 1,
                    UpdatedAtUtc = nowUtc
                })
                .ToArray();
            foreach (var run in claimed) await Save(run);
            return claimed;
        }
        finally { Gate.Release(); }
    }

    private List<AiRun> ReadAll() => StorageFile.ReadExistingFiles(_path, "*.json")
        .Select(entry => Deserialize(entry.Content))
        .ToList();

    private Task Save(AiRun run) =>
        StorageFile.WriteAllTextAtomicAsync(File(run.Id), SafeStorageJson.Serialize(run));

    private string File(Guid id) => Path.Combine(_path, $"{id:N}.json");
    private static AiRun Deserialize(string content) => SafeStorageJson.Deserialize<AiRun>(content);
    private static AiRunWriteResult Written(AiRun run) => new(AiRunWriteStatus.Written, run, run.Revision);
    private static AiRunWriteResult Conflict(AiRun run) => new(AiRunWriteStatus.Conflict, run, run.Revision);

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("AI run ID is required.", nameof(id));
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("AI run timestamps must be UTC.", nameof(value));
    }
}
