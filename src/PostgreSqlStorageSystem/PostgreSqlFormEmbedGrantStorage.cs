using Model;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>DELETE RETURNING verhindert Replay unabhängig von der Anzahl der API-Prozesse.</summary>
internal sealed class PostgreSqlFormEmbedGrantStorage(PostgreSqlSession session) : IFormEmbedGrantStorage
{
    public Task Create(FormEmbedGrant grant, DateTimeOffset utcNow) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            -- Ausschließlich die bereits gesperrte eigene Aufgabe: niemals fremde
            -- Freigaben sperren, während die Aufgabenzeile gehalten wird.
            DELETE FROM {schema}.form_embed_grants WHERE secret_hash IN (
                SELECT secret_hash FROM {schema}.form_embed_grants
                WHERE user_task_id = @taskId ORDER BY expires_at DESC, secret_hash OFFSET 3
            );
            INSERT INTO {schema}.form_embed_grants (secret_hash, user_task_id, expires_at, body)
            VALUES (@hash, @taskId, @expiresAt, @body);
            """);
        command.Parameters.AddWithValue("hash", grant.SecretHash);
        command.Parameters.AddWithValue("taskId", grant.UserTaskId);
        command.Parameters.AddWithValue("expiresAt", grant.ExpiresAtUtc);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(grant));
        await command.ExecuteNonQueryAsync();
    });

    public Task<FormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.form_embed_grants WHERE secret_hash = @hash AND expires_at > @now
                """);
            command.Parameters.AddWithValue("hash", secretHash);
            command.Parameters.AddWithValue("now", utcNow);
            var body = await command.ExecuteScalarAsync(cancellationToken);
            return body is string json ? StorageJson.Deserialize<FormEmbedGrant>(json) : null;
        });

    public Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                DELETE FROM {schema}.form_embed_grants WHERE secret_hash IN (
                    SELECT secret_hash FROM {schema}.form_embed_grants WHERE expires_at <= @now
                    ORDER BY expires_at LIMIT 200 FOR UPDATE SKIP LOCKED
                )
                """);
            command.Parameters.AddWithValue("now", utcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });

    public Task<FormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            DELETE FROM {schema}.form_embed_grants WHERE secret_hash = @hash
            RETURNING body, expires_at
            """);
        command.Parameters.AddWithValue("hash", secretHash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetFieldValue<DateTimeOffset>(1) <= utcNow) return null;
        return StorageJson.Deserialize<FormEmbedGrant>(reader.GetString(0));
    });
}
