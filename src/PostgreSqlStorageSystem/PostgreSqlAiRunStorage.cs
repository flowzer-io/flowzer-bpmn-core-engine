using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-Ablage fuer KI-Läufe. Querybare Zustandsfelder und Zeilensperren halten Claim,
/// Lease, Recovery und CAS auch bei mehreren API-Prozessen in einer Transaktion konsistent.
/// </summary>
internal sealed class PostgreSqlAiRunStorage(PostgreSqlSession session) : IAiRunStorage
{
    private const string Columns = "body, status, attempt, maximum_attempts, revision, created_at, updated_at, "
                                   + "next_attempt_at, lease_owner, lease_expires_at, provider_call_started_at, "
                                   + "output_json, result_model, input_tokens, output_tokens, total_tokens, failure_code";

    public Task<AiRunWriteResult> TryCreate(AiRun run) => session.RunAsync(async (connection, transaction) =>
    {
        AiRunStorageRules.ValidateNew(run);
        await using var command = session.CreateCommand(connection, transaction, $$"""
            INSERT INTO {schema}.ai_runs
                (id, process_instance_id, token_id, status, attempt, maximum_attempts, revision,
                 created_at, updated_at, next_attempt_at, lease_owner, lease_expires_at,
                 provider_call_started_at, output_json, result_model, input_tokens,
                 output_tokens, total_tokens, failure_code, body)
            VALUES
                (@id, @instanceId, @tokenId, @status, @attempt, @maximumAttempts, @revision,
                 @createdAt, @updatedAt, @nextAttemptAt, @leaseOwner, @leaseExpiresAt,
                 @providerCallStartedAt, @outputJson, @resultModel, @inputTokens,
                 @outputTokens, @totalTokens, @failureCode, @body)
            ON CONFLICT DO NOTHING
            RETURNING {{Columns}}
            """);
        AddAllParameters(command, run);
        var created = (await ReadRuns(command)).SingleOrDefault();
        if (created is not null) return Written(created);

        var current = await ReadCurrentByIdentity(connection, transaction, run);
        return new AiRunWriteResult(
            AiRunWriteStatus.Conflict,
            current,
            current?.Revision ?? 0);
    });

    public Task<AiRun?> Get(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateId(id);
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {Columns} FROM {{schema}}.ai_runs WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return (await ReadRuns(command)).SingleOrDefault();
    });

    public Task<IReadOnlyList<AiRun>> List() => session.RunAsync<IReadOnlyList<AiRun>>(
        async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                $"SELECT {Columns} FROM {{schema}}.ai_runs ORDER BY created_at, id");
            return await ReadRuns(command);
        });

    public Task<IReadOnlyList<AiRun>> ClaimProviderRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns)
    {
        AiRunStorageRules.ValidateLeaseArguments(leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
        return session.RunAsync<IReadOnlyList<AiRun>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, $$"""
                UPDATE {schema}.ai_runs AS target
                SET status = @running,
                    lease_owner = @leaseOwner,
                    lease_expires_at = @leaseExpiresAt,
                    provider_call_started_at = NULL,
                    next_attempt_at = NULL,
                    failure_code = NULL,
                    revision = target.revision + 1,
                    updated_at = @now
                WHERE target.id IN (
                    SELECT candidate.id
                    FROM {schema}.ai_runs AS candidate
                    WHERE (candidate.status = @pending
                           OR (candidate.status = @retryScheduled AND candidate.next_attempt_at <= @now))
                      AND candidate.lease_owner IS NULL
                      AND candidate.lease_expires_at IS NULL
                      AND candidate.attempt < candidate.maximum_attempts
                    ORDER BY COALESCE(candidate.next_attempt_at, candidate.created_at), candidate.id
                    LIMIT @maxRuns
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING {{Columns}}
                """);
            AddClaimParameters(command, leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
            AddStatus(command, "running", AiRunStatus.Running);
            AddStatus(command, "pending", AiRunStatus.Pending);
            AddStatus(command, "retryScheduled", AiRunStatus.RetryScheduled);
            return await ReadRuns(command);
        });
    }

    public Task<IReadOnlyList<AiRun>> ClaimResultRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns)
    {
        AiRunStorageRules.ValidateLeaseArguments(leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
        return session.RunAsync<IReadOnlyList<AiRun>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, $$"""
                UPDATE {schema}.ai_runs AS target
                SET status = @completing,
                    lease_owner = @leaseOwner,
                    lease_expires_at = @leaseExpiresAt,
                    next_attempt_at = NULL,
                    revision = target.revision + 1,
                    updated_at = @now
                WHERE target.id IN (
                    SELECT candidate.id
                    FROM {schema}.ai_runs AS candidate
                    WHERE candidate.status = @resultReady
                      AND candidate.lease_owner IS NULL
                      AND candidate.lease_expires_at IS NULL
                    ORDER BY candidate.updated_at, candidate.id
                    LIMIT @maxRuns
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING {{Columns}}
                """);
            AddClaimParameters(command, leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
            AddStatus(command, "completing", AiRunStatus.Completing);
            AddStatus(command, "resultReady", AiRunStatus.ResultReady);
            return await ReadRuns(command);
        });
    }

    public Task<AiRun?> RenewLease(
        Guid id,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc)
    {
        ValidateId(id);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        AiRunStorageRules.ValidateLeaseArguments(leaseOwner, nowUtc, leaseExpiresAtUtc);
        return session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, $$"""
                UPDATE {schema}.ai_runs AS target
                SET lease_expires_at = GREATEST(target.lease_expires_at, @leaseExpiresAt),
                    revision = CASE WHEN target.lease_expires_at < @leaseExpiresAt
                                    THEN target.revision + 1 ELSE target.revision END,
                    updated_at = CASE WHEN target.lease_expires_at < @leaseExpiresAt
                                      THEN @now ELSE target.updated_at END
                WHERE target.id = @id
                  AND target.revision = @expectedRevision
                  AND target.status IN (@running, @completing)
                  AND target.lease_owner = @leaseOwner
                  AND target.lease_expires_at > @now
                RETURNING {{Columns}}
                """);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("expectedRevision", expectedRevision);
            command.Parameters.AddWithValue("leaseOwner", leaseOwner);
            AddTimestamp(command, "leaseExpiresAt", leaseExpiresAtUtc);
            AddTimestamp(command, "now", nowUtc);
            AddStatus(command, "running", AiRunStatus.Running);
            AddStatus(command, "completing", AiRunStatus.Completing);
            return (await ReadRuns(command)).SingleOrDefault();
        });
    }

    public Task<AiRunWriteResult> TryUpdate(
        AiRun updated,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc) => session.RunAsync(async (connection, transaction) =>
    {
        ArgumentNullException.ThrowIfNull(updated);
        ValidateId(updated.Id);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        EnsureUtc(nowUtc);

        var current = await ReadForUpdate(connection, transaction, updated.Id);
        if (current is null) return new AiRunWriteResult(AiRunWriteStatus.NotFound, null, 0);
        if (current.Revision != expectedRevision) return Conflict(current);
        if (!string.Equals(current.LeaseOwner, leaseOwner, StringComparison.Ordinal)
            || current.LeaseExpiresAtUtc is null
            || current.LeaseExpiresAtUtc <= nowUtc)
            return new AiRunWriteResult(AiRunWriteStatus.LeaseLost, current, current.Revision);

        AiRunStorageRules.ValidateTransition(current, updated, expectedRevision, leaseOwner, nowUtc);
        await using var command = session.CreateCommand(connection, transaction, $$"""
            UPDATE {schema}.ai_runs
            SET status = @status,
                attempt = @attempt,
                revision = @revision,
                updated_at = @updatedAt,
                next_attempt_at = @nextAttemptAt,
                lease_owner = @leaseOwner,
                lease_expires_at = @leaseExpiresAt,
                provider_call_started_at = @providerCallStartedAt,
                output_json = @outputJson,
                result_model = @resultModel,
                input_tokens = @inputTokens,
                output_tokens = @outputTokens,
                total_tokens = @totalTokens,
                failure_code = @failureCode
            WHERE id = @id AND revision = @expectedRevision
            RETURNING {{Columns}}
            """);
        AddMutableParameters(command, updated);
        command.Parameters.AddWithValue("id", updated.Id);
        command.Parameters.AddWithValue("expectedRevision", expectedRevision);
        var written = (await ReadRuns(command)).SingleOrDefault();
        return written is null ? Conflict(current) : Written(written);
    });

    public Task<IReadOnlyList<AiRun>> RecoverExpiredLeases(DateTime nowUtc, int maxRuns)
    {
        EnsureUtc(nowUtc);
        if (maxRuns is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxRuns));
        return session.RunAsync<IReadOnlyList<AiRun>>(async (connection, transaction) =>
        {
            await using var select = session.CreateCommand(connection, transaction, $$"""
                SELECT {{Columns}}
                FROM {schema}.ai_runs
                WHERE status IN (@running, @completing)
                  AND lease_expires_at <= @now
                ORDER BY lease_expires_at, id
                LIMIT @maxRuns
                FOR UPDATE SKIP LOCKED
                """);
            AddStatus(select, "running", AiRunStatus.Running);
            AddStatus(select, "completing", AiRunStatus.Completing);
            AddTimestamp(select, "now", nowUtc);
            select.Parameters.AddWithValue("maxRuns", maxRuns);
            var expired = await ReadRuns(select);
            var recovered = new List<AiRun>(expired.Count);
            foreach (var current in expired)
            {
                var updated = Recover(current, nowUtc);
                await using var update = session.CreateCommand(connection, transaction, $$"""
                    UPDATE {schema}.ai_runs
                    SET status = @status,
                        revision = @revision,
                        updated_at = @updatedAt,
                        lease_owner = NULL,
                        lease_expires_at = NULL,
                        failure_code = @failureCode
                    WHERE id = @id AND revision = @expectedRevision
                    RETURNING {{Columns}}
                    """);
                AddStatus(update, "status", updated.Status);
                update.Parameters.AddWithValue("revision", updated.Revision);
                AddTimestamp(update, "updatedAt", updated.UpdatedAtUtc);
                AddNullableText(update, "failureCode", updated.FailureCode);
                update.Parameters.AddWithValue("id", updated.Id);
                update.Parameters.AddWithValue("expectedRevision", current.Revision);
                var written = (await ReadRuns(update)).Single();
                recovered.Add(written);
            }
            return recovered;
        });
    }

    private async Task<AiRun?> ReadCurrentByIdentity(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AiRun run)
    {
        await using var command = session.CreateCommand(connection, transaction, $$"""
            SELECT {{Columns}}
            FROM {schema}.ai_runs
            WHERE id = @id OR (process_instance_id = @instanceId AND token_id = @tokenId)
            ORDER BY (id = @id) DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue("id", run.Id);
        command.Parameters.AddWithValue("instanceId", run.ProcessInstanceId);
        command.Parameters.AddWithValue("tokenId", run.TokenId);
        return (await ReadRuns(command)).SingleOrDefault();
    }

    private async Task<AiRun?> ReadForUpdate(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id)
    {
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {Columns} FROM {{schema}}.ai_runs WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("id", id);
        return (await ReadRuns(command)).SingleOrDefault();
    }

    private static async Task<List<AiRun>> ReadRuns(NpgsqlCommand command)
    {
        var runs = new List<AiRun>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var snapshot = StorageJson.DeserializeConcrete<AiRun>(reader.GetString(0));
            runs.Add(snapshot with
            {
                Status = (AiRunStatus)reader.GetInt16(1),
                Attempt = reader.GetInt32(2),
                MaximumAttempts = reader.GetInt32(3),
                Revision = reader.GetInt64(4),
                CreatedAtUtc = reader.GetDateTime(5),
                UpdatedAtUtc = reader.GetDateTime(6),
                NextAttemptAtUtc = OptionalTimestamp(reader, 7),
                LeaseOwner = reader.IsDBNull(8) ? null : reader.GetString(8),
                LeaseExpiresAtUtc = OptionalTimestamp(reader, 9),
                ProviderCallStartedAtUtc = OptionalTimestamp(reader, 10),
                OutputJson = reader.IsDBNull(11) ? null : reader.GetString(11),
                ResultModel = reader.IsDBNull(12) ? null : reader.GetString(12),
                InputTokens = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                OutputTokens = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                TotalTokens = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                FailureCode = reader.IsDBNull(16) ? null : reader.GetString(16)
            });
        }
        return runs;
    }

    private static AiRun Recover(AiRun current, DateTime nowUtc) => current.Status switch
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

    private static void AddAllParameters(NpgsqlCommand command, AiRun run)
    {
        command.Parameters.AddWithValue("id", run.Id);
        command.Parameters.AddWithValue("instanceId", run.ProcessInstanceId);
        command.Parameters.AddWithValue("tokenId", run.TokenId);
        command.Parameters.AddWithValue("maximumAttempts", run.MaximumAttempts);
        AddTimestamp(command, "createdAt", run.CreatedAtUtc);
        command.Parameters.AddWithValue("body", StorageJson.SerializeConcrete(run));
        AddMutableParameters(command, run);
    }

    private static void AddMutableParameters(NpgsqlCommand command, AiRun run)
    {
        AddStatus(command, "status", run.Status);
        command.Parameters.AddWithValue("attempt", run.Attempt);
        command.Parameters.AddWithValue("revision", run.Revision);
        AddTimestamp(command, "updatedAt", run.UpdatedAtUtc);
        AddTimestamp(command, "nextAttemptAt", run.NextAttemptAtUtc);
        AddNullableText(command, "leaseOwner", run.LeaseOwner);
        AddTimestamp(command, "leaseExpiresAt", run.LeaseExpiresAtUtc);
        AddTimestamp(command, "providerCallStartedAt", run.ProviderCallStartedAtUtc);
        AddNullableText(command, "outputJson", run.OutputJson);
        AddNullableText(command, "resultModel", run.ResultModel);
        AddNullableInteger(command, "inputTokens", run.InputTokens);
        AddNullableInteger(command, "outputTokens", run.OutputTokens);
        AddNullableInteger(command, "totalTokens", run.TotalTokens);
        AddNullableText(command, "failureCode", run.FailureCode);
    }

    private static void AddClaimParameters(
        NpgsqlCommand command,
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns)
    {
        command.Parameters.AddWithValue("leaseOwner", leaseOwner);
        AddTimestamp(command, "now", nowUtc);
        AddTimestamp(command, "leaseExpiresAt", leaseExpiresAtUtc);
        command.Parameters.AddWithValue("maxRuns", maxRuns);
    }

    private static void AddStatus(NpgsqlCommand command, string name, AiRunStatus value)
    {
        var parameter = command.Parameters.AddWithValue(name, (short)value);
        parameter.NpgsqlDbType = NpgsqlDbType.Smallint;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.Text;
    }

    private static void AddNullableInteger(NpgsqlCommand command, string name, int? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.Integer;
    }

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTime? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
    }

    private static DateTime? OptionalTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static AiRunWriteResult Written(AiRun run) =>
        new(AiRunWriteStatus.Written, run, run.Revision);

    private static AiRunWriteResult Conflict(AiRun run) =>
        new(AiRunWriteStatus.Conflict, run, run.Revision);

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
