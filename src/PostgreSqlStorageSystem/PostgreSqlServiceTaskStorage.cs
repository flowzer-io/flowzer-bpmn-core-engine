using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Auftraege und Webhook-Anmeldungen in PostgreSQL.
///
/// Der Vergabezustand liegt in eigenen Spalten, nicht im JSON-Koerper: Nur so kann ein Auftrag
/// in einem einzigen Statement uebernommen werden. Beim Lesen werden die Spalten wieder in das
/// Objekt gehoben, damit Aufrufer nur mit <see cref="ServiceTaskJob"/> arbeiten.
/// </summary>
internal sealed class PostgreSqlServiceTaskStorage(PostgreSqlSession session) : IServiceTaskStorage
{
    private const string JobColumns = "body, locked_until, locked_by, retry_at, retries, last_error";

    public Task SaveJob(ServiceTaskJob job) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.service_task_jobs
                (id, type, process_instance_id, token_id, created_at, locked_until, locked_by, retry_at, retries, last_error, body)
            VALUES (@id, @type, @instanceId, @tokenId, @createdAt, @lockedUntil, @lockedBy, @retryAt, @retries, @lastError, @body)
            ON CONFLICT (id) DO UPDATE SET
                type = EXCLUDED.type,
                process_instance_id = EXCLUDED.process_instance_id,
                token_id = EXCLUDED.token_id,
                created_at = EXCLUDED.created_at,
                locked_until = EXCLUDED.locked_until,
                locked_by = EXCLUDED.locked_by,
                retry_at = EXCLUDED.retry_at,
                retries = EXCLUDED.retries,
                last_error = EXCLUDED.last_error,
                body = EXCLUDED.body
            """);
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync();
    });

    /// <summary>
    /// Sucht die freien Auftraege, sperrt ihre Zeilen und schreibt die Uebernahme in einem
    /// Statement. <c>SKIP LOCKED</c> laesst zwei gleichzeitige Aufrufe verschiedene Auftraege
    /// bekommen, statt einander zu blockieren.
    /// </summary>
    public Task<IReadOnlyList<ServiceTaskJob>> ClaimJobs(string type, string lockOwner, DateTime now, DateTime lockedUntil, int maxJobs) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, $$"""
                UPDATE {schema}.service_task_jobs AS target
                SET locked_by = @lockOwner, locked_until = @lockedUntil
                WHERE target.id IN (
                    SELECT candidate.id
                    FROM {schema}.service_task_jobs AS candidate
                    WHERE candidate.type = @type
                      AND candidate.retries > 0
                      AND (candidate.locked_until IS NULL OR candidate.locked_until <= @now)
                      AND (candidate.retry_at IS NULL OR candidate.retry_at <= @now)
                    ORDER BY candidate.created_at
                    LIMIT @maxJobs
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING {{JobColumns}}
                """);
            command.Parameters.AddWithValue("type", type);
            command.Parameters.AddWithValue("lockOwner", lockOwner);
            AddTimestamp(command, "lockedUntil", lockedUntil);
            AddTimestamp(command, "now", now);
            command.Parameters.AddWithValue("maxJobs", maxJobs);

            return (IReadOnlyList<ServiceTaskJob>)await ReadJobs(command);
        });

    public Task<ServiceTaskJob?> GetLockedJob(Guid jobId, string lockOwner, DateTime now) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, $$"""
                SELECT {{JobColumns}} FROM {schema}.service_task_jobs
                WHERE id = @id AND locked_by = @lockOwner AND locked_until > @now
                """);
            command.Parameters.AddWithValue("id", jobId);
            command.Parameters.AddWithValue("lockOwner", lockOwner);
            AddTimestamp(command, "now", now);

            return (await ReadJobs(command)).SingleOrDefault();
        });

    public Task<ServiceTaskJob?> GetJob(Guid jobId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {JobColumns} FROM {{schema}}.service_task_jobs WHERE id = @id");
        command.Parameters.AddWithValue("id", jobId);
        return (await ReadJobs(command)).SingleOrDefault();
    });

    public Task<IEnumerable<ServiceTaskJob>> GetJobs() => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {JobColumns} FROM {{schema}}.service_task_jobs ORDER BY created_at");
        return (IEnumerable<ServiceTaskJob>)await ReadJobs(command);
    });

    public Task<IEnumerable<ServiceTaskJob>> GetJobsByType(string type) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {JobColumns} FROM {{schema}}.service_task_jobs WHERE type = @type ORDER BY created_at");
        command.Parameters.AddWithValue("type", type);
        return (IEnumerable<ServiceTaskJob>)await ReadJobs(command);
    });

    public Task RemoveJob(Guid jobId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.service_task_jobs WHERE id = @id");
        command.Parameters.AddWithValue("id", jobId);
        await command.ExecuteNonQueryAsync();
    });

    public Task RemoveJobsByInstanceId(Guid processInstanceId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.service_task_jobs WHERE process_instance_id = @instanceId");
        command.Parameters.AddWithValue("instanceId", processInstanceId);
        await command.ExecuteNonQueryAsync();
    });

    public Task SaveWebhook(ServiceTaskWebhook webhook) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.service_task_webhooks (id, type, body)
            VALUES (@id, @type, @body)
            ON CONFLICT (id) DO UPDATE SET type = EXCLUDED.type, body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", webhook.Id);
        command.Parameters.AddWithValue("type", webhook.Type);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(webhook));
        await command.ExecuteNonQueryAsync();
    });

    public Task<ServiceTaskWebhook?> GetWebhook(Guid webhookId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.service_task_webhooks WHERE id = @id");
        command.Parameters.AddWithValue("id", webhookId);
        return await command.ExecuteScalarAsync() is string body ? StorageJson.Deserialize<ServiceTaskWebhook>(body) : null;
    });

    public Task<IEnumerable<ServiceTaskWebhook>> GetWebhooks() => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.service_task_webhooks ORDER BY type");
        var webhooks = new List<ServiceTaskWebhook>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            webhooks.Add(StorageJson.Deserialize<ServiceTaskWebhook>(reader.GetString(0)));
        }

        return (IEnumerable<ServiceTaskWebhook>)webhooks;
    });

    public Task RemoveWebhook(Guid webhookId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.service_task_webhooks WHERE id = @id");
        command.Parameters.AddWithValue("id", webhookId);
        await command.ExecuteNonQueryAsync();
    });

    private static void AddJobParameters(NpgsqlCommand command, ServiceTaskJob job)
    {
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("type", job.Type);
        command.Parameters.AddWithValue("instanceId", job.ProcessInstanceId);
        command.Parameters.AddWithValue("tokenId", job.Token.Id);
        AddTimestamp(command, "createdAt", job.CreatedAt);
        AddTimestamp(command, "lockedUntil", job.LockedUntil);
        command.Parameters.AddWithValue("lockedBy", (object?)job.LockedBy ?? DBNull.Value).NpgsqlDbType = NpgsqlDbType.Text;
        AddTimestamp(command, "retryAt", job.RetryAt);
        command.Parameters.AddWithValue("retries", job.Retries);
        command.Parameters.AddWithValue("lastError", (object?)job.LastErrorMessage ?? DBNull.Value).NpgsqlDbType = NpgsqlDbType.Text;
        command.Parameters.AddWithValue("body", StorageJson.Serialize(job));
    }

    /// <summary>Der Vergabezustand kommt aus den Spalten, nicht aus dem gespeicherten Koerper.</summary>
    private static async Task<List<ServiceTaskJob>> ReadJobs(NpgsqlCommand command)
    {
        var jobs = new List<ServiceTaskJob>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var job = StorageJson.Deserialize<ServiceTaskJob>(reader.GetString(0));
            job.LockedUntil = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            job.LockedBy = reader.IsDBNull(2) ? null : reader.GetString(2);
            job.RetryAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            job.Retries = reader.GetInt32(4);
            job.LastErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5);
            jobs.Add(job);
        }

        return jobs;
    }

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTime? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
    }
}
