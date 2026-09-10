using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Dateibasierte Ablage des Identitaetsverzeichnisses. Snapshot und Status liegen bewusst in
/// genau einem Dokument: Das atomare Umbenennen veroeffentlicht nie einen neuen Snapshot mit
/// altem Status (oder umgekehrt). Sie bleibt wie die uebrige Dateiablage ein Einzelprozesspfad.
/// </summary>
internal sealed class IdentityDirectoryStorage(Storage storage) : IIdentityDirectoryStorage
{
    private const string StateFileName = "identity-directory.json";
    private readonly string _path = Path.Combine(storage.GetBasePath("FileStorage/IdentityDirectory"), StateFileName);
    private readonly JsonSerializerSettings _settings = storage.NewtonSoftDefaultSettings;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<DirectorySnapshot?> GetActiveSnapshot() => (await ReadStateAsync()).ActiveSnapshot;

    public async Task<DirectorySyncStatus?> GetSyncStatus() => (await ReadStateAsync()).SyncStatus;

    public async Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ValidateLease(generationId, startedAtUtc, leaseExpiresAtUtc);
        await _writeLock.WaitAsync();
        try
        {
            var state = await ReadStateAsync();
            var prior = state.SyncStatus;
            if (prior?.State == DirectorySyncState.Running && prior.LeaseExpiresAtUtc > startedAtUtc.ToUniversalTime())
            {
                return false;
            }

            state.SyncStatus = CreateRunningStatus(issuer, generationId, startedAtUtc, leaseExpiresAtUtc, prior);
            await WriteStateAsync(state);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> FailSync(
        string issuer,
        Guid generationId,
        string errorCode,
        string errorMessage,
        DateTime failedAtUtc)
    {
        await _writeLock.WaitAsync();
        try
        {
            var state = await ReadStateAsync();
            var prior = state.SyncStatus;
            if (!IsExpectedRunningGeneration(prior, issuer, generationId))
            {
                return false;
            }

            state.SyncStatus = CreateFailedStatus(prior!, errorCode, errorMessage, failedAtUtc);
            await WriteStateAsync(state);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task PublishSnapshot(DirectorySnapshot snapshot)
    {
        await _writeLock.WaitAsync();
        try
        {
            var state = await ReadStateAsync();
            ValidateExpectedGeneration(state.SyncStatus, snapshot);
            var runningStatus = state.SyncStatus!;
            state.ActiveSnapshot = DirectorySnapshotPublisher.Merge(state.ActiveSnapshot, snapshot);
            state.SyncStatus = DirectorySnapshotPublisher.CreateSucceededStatus(state.ActiveSnapshot, runningStatus);
            await WriteStateAsync(state);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<IdentityDirectoryState> ReadStateAsync()
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(_path);
        return content is null
            ? new IdentityDirectoryState()
            : JsonConvert.DeserializeObject<IdentityDirectoryState>(content, _settings)
              ?? throw new InvalidDataException("Stored identity-directory state could not be read.");
    }

    private Task WriteStateAsync(IdentityDirectoryState state) =>
        StorageFile.WriteAllTextAtomicAsync(_path, JsonConvert.SerializeObject(state, _settings));

    private static void ValidateExpectedGeneration(DirectorySyncStatus? status, DirectorySnapshot snapshot)
    {
        if (status?.State != DirectorySyncState.Running
            || status.RunningGenerationId != snapshot.GenerationId
            || !string.Equals(status.Issuer, snapshot.Issuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The directory snapshot does not belong to the currently running synchronization.");
        }
    }

    private static bool IsExpectedRunningGeneration(DirectorySyncStatus? status, string issuer, Guid generationId) =>
        status?.State == DirectorySyncState.Running
        && status.RunningGenerationId == generationId
        && string.Equals(status.Issuer, issuer, StringComparison.Ordinal);

    private static void ValidateLease(Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc)
    {
        if (generationId == Guid.Empty || startedAtUtc == default || leaseExpiresAtUtc == default
            || leaseExpiresAtUtc.ToUniversalTime() <= startedAtUtc.ToUniversalTime())
        {
            throw new ArgumentException("A directory synchronization requires an ID and a finite lease after its start time.");
        }
    }

    private static DirectorySyncStatus CreateRunningStatus(
        string issuer,
        Guid generationId,
        DateTime startedAtUtc,
        DateTime leaseExpiresAtUtc,
        DirectorySyncStatus? prior) => new()
    {
        State = DirectorySyncState.Running,
        Issuer = issuer,
        ActiveGenerationId = prior?.ActiveGenerationId,
        RunningGenerationId = generationId,
        AttemptedAtUtc = startedAtUtc.ToUniversalTime(),
        LeaseExpiresAtUtc = leaseExpiresAtUtc.ToUniversalTime(),
        SucceededAtUtc = prior?.SucceededAtUtc,
        FailedAtUtc = prior?.FailedAtUtc,
        UserCount = prior?.UserCount ?? 0,
        GroupCount = prior?.GroupCount ?? 0,
        MembershipCount = prior?.MembershipCount ?? 0
    };

    private static DirectorySyncStatus CreateFailedStatus(
        DirectorySyncStatus prior,
        string errorCode,
        string errorMessage,
        DateTime failedAtUtc) => new()
    {
        State = DirectorySyncState.Failed,
        Issuer = prior.Issuer,
        ActiveGenerationId = prior.ActiveGenerationId,
        AttemptedAtUtc = prior.AttemptedAtUtc,
        SucceededAtUtc = prior.SucceededAtUtc,
        FailedAtUtc = failedAtUtc.ToUniversalTime(),
        UserCount = prior.UserCount,
        GroupCount = prior.GroupCount,
        MembershipCount = prior.MembershipCount,
        ErrorCode = DirectorySyncStatus.SanitizeError(errorCode, 128),
        ErrorMessage = DirectorySyncStatus.SanitizeError(errorMessage, 512)
    };

    private sealed class IdentityDirectoryState
    {
        public DirectorySnapshot? ActiveSnapshot { get; set; }
        public DirectorySyncStatus? SyncStatus { get; set; }
    }
}
