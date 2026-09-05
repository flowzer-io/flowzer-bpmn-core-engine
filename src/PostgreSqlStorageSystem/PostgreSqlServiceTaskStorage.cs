using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Auftraege und Webhook-Anmeldungen in PostgreSQL. Die Vergabespalten liegen ausserhalb des
/// JSON-Koerpers, damit die Suche nach freien Auftraegen ohne Volltextzugriff auskommt.
/// </summary>
internal sealed class PostgreSqlServiceTaskStorage(PostgreSqlSession session) : IServiceTaskStorage
{
    public Task SaveJob(ServiceTaskJob job) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.service_task_jobs (id, type, process_instance_id, locked_until, retry_at, retries, body)
            VALUES (@id, @type, @instanceId, @lockedUntil, @retryAt, @retries, @body)
            ON CONFLICT (id) DO UPDATE SET
                type = EXCLUDED.type,
                process_instance_id = EXCLUDED.process_instance_id,
                locked_until = EXCLUDED.locked_until,
                retry_at = EXCLUDED.retry_at,
                retries = EXCLUDED.retries,
                body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("type", job.Type);
        command.Parameters.AddWithValue("instanceId", job.ProcessInstanceId);
        AddNullableTimestamp(command, "lockedUntil", job.LockedUntil);
        AddNullableTimestamp(command, "retryAt", job.RetryAt);
        command.Parameters.AddWithValue("retries", job.Retries);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(job));
        await command.ExecuteNonQueryAsync();
    });

    public Task<ServiceTaskJob?> GetJob(Guid jobId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.service_task_jobs WHERE id = @id");
        command.Parameters.AddWithValue("id", jobId);
        return await command.ExecuteScalarAsync() is string body ? StorageJson.Deserialize<ServiceTaskJob>(body) : null;
    });

    public Task<IEnumerable<ServiceTaskJob>> GetJobs() =>
        QueryJobs("SELECT body FROM {schema}.service_task_jobs", []);

    public Task<IEnumerable<ServiceTaskJob>> GetJobsByType(string type) =>
        QueryJobs("SELECT body FROM {schema}.service_task_jobs WHERE type = @type", [("type", type)]);

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

    private Task<IEnumerable<ServiceTaskJob>> QueryJobs(string sql, (string Name, object Value)[] parameters) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, sql);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var jobs = new List<ServiceTaskJob>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(StorageJson.Deserialize<ServiceTaskJob>(reader.GetString(0)));
            }

            return (IEnumerable<ServiceTaskJob>)jobs;
        });

    /// <summary>Nullable Zeitstempel typisiert uebergeben, damit Npgsql bei DBNull nicht raten muss.</summary>
    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTime? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
    }
}
