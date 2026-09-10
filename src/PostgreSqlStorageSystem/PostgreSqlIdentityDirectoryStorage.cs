using Npgsql;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-Ablage fuer den atomar aktiven Verzeichnisstand. Die einzelne State-Zeile wird
/// beim Publizieren gesperrt; damit kann ein aelterer Import weder einen juengeren Snapshot noch
/// dessen Status nachtraeglich ersetzen.
/// </summary>
internal sealed class PostgreSqlIdentityDirectoryStorage(PostgreSqlSession session) : IIdentityDirectoryStorage
{
    public Task<DirectorySnapshot?> GetActiveSnapshot() => session.RunAsync(async (connection, transaction) =>
    {
        var state = await ReadStateAsync(connection, transaction, lockForUpdate: false);
        return state.ActiveSnapshot;
    });

    public Task<DirectorySyncStatus?> GetSyncStatus() => session.RunAsync(async (connection, transaction) =>
    {
        var state = await ReadStateAsync(connection, transaction, lockForUpdate: false);
        return state.SyncStatus;
    });

    public Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc) => session.RunAsync(async (connection, transaction) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ValidateLease(generationId, startedAtUtc, leaseExpiresAtUtc);
        var state = await ReadStateAsync(connection, transaction, lockForUpdate: true);
        var prior = state.SyncStatus;
        if (prior?.State == DirectorySyncState.Running && prior.LeaseExpiresAtUtc > startedAtUtc.ToUniversalTime())
        {
            return false;
        }

        state.SyncStatus = CreateRunningStatus(issuer, generationId, startedAtUtc, leaseExpiresAtUtc, prior);
        await WriteStateAsync(connection, transaction, state);
        return true;
    });

    public Task<bool> FailSync(
        string issuer,
        Guid generationId,
        string errorCode,
        string errorMessage,
        DateTime failedAtUtc) => session.RunAsync(async (connection, transaction) =>
    {
        var state = await ReadStateAsync(connection, transaction, lockForUpdate: true);
        var prior = state.SyncStatus;
        if (!IsExpectedRunningGeneration(prior, issuer, generationId))
        {
            return false;
        }

        state.SyncStatus = CreateFailedStatus(prior!, errorCode, errorMessage, failedAtUtc);
        await WriteStateAsync(connection, transaction, state);
        return true;
    });

    public Task PublishSnapshot(DirectorySnapshot snapshot) => session.RunAsync(async (connection, transaction) =>
    {
        var state = await ReadStateAsync(connection, transaction, lockForUpdate: true);
        ValidateExpectedGeneration(state.SyncStatus, snapshot);
        var runningStatus = state.SyncStatus!;
        state.ActiveSnapshot = DirectorySnapshotPublisher.Merge(state.ActiveSnapshot, snapshot);
        state.SyncStatus = DirectorySnapshotPublisher.CreateSucceededStatus(state.ActiveSnapshot, runningStatus);
        await WriteStateAsync(connection, transaction, state);
    });

    private async Task<IdentityDirectoryState> ReadStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool lockForUpdate)
    {
        await using (var create = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.identity_directory_state (singleton) VALUES (true)
            ON CONFLICT (singleton) DO NOTHING
            """))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT active_snapshot, sync_status FROM {{schema}}.identity_directory_state WHERE singleton = true{(lockForUpdate ? " FOR UPDATE" : string.Empty)}");
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Identity-directory state could not be initialized.");
        return new IdentityDirectoryState
        {
            ActiveSnapshot = reader.IsDBNull(0) ? null : StorageJson.Deserialize<DirectorySnapshot>(reader.GetString(0)),
            SyncStatus = reader.IsDBNull(1) ? null : StorageJson.Deserialize<DirectorySyncStatus>(reader.GetString(1))
        };
    }

    private async Task WriteStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, IdentityDirectoryState state)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            UPDATE {schema}.identity_directory_state
            SET active_snapshot = @activeSnapshot, sync_status = @syncStatus
            WHERE singleton = true
            """);
        command.Parameters.AddWithValue("activeSnapshot", (object?)state.ActiveSnapshot is null ? DBNull.Value : StorageJson.Serialize(state.ActiveSnapshot));
        command.Parameters.AddWithValue("syncStatus", (object?)state.SyncStatus is null ? DBNull.Value : StorageJson.Serialize(state.SyncStatus));
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Identity-directory state was lost.");
    }

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
