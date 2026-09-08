using StorageSystem;

namespace PostgreSqlStorageSystem;

internal sealed class PostgreSqlIdempotencyStorage(PostgreSqlSession session) : IIdempotencyStorage
{
    public Task<IdempotencyRecord?> Get(string scopeHash) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            SELECT request_hash, operation, created_at, expires_at, is_completed, process_instance_id
            FROM {schema}.idempotency_records WHERE scope_hash = @scopeHash
            """);
        command.Parameters.AddWithValue("scopeHash", scopeHash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new IdempotencyRecord
        {
            ScopeHash = scopeHash, RequestHash = reader.GetString(0), Operation = reader.GetString(1),
            CreatedAt = reader.GetDateTime(2).ToUniversalTime(), ExpiresAt = reader.GetDateTime(3).ToUniversalTime(),
            IsCompleted = reader.GetBoolean(4), ProcessInstanceId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
        };
    });

    public Task<bool> TryCreate(IdempotencyRecord record) => session.RunAsync(async (connection, transaction) =>
    {
        // ON CONFLICT wartet auf konkurrierende uncommitted Inserts und liefert danach 0.
        // Der folgende Read in derselben READ-COMMITTED-Transaktion sieht den Sieger.
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.idempotency_records
                (scope_hash, request_hash, operation, created_at, expires_at, is_completed, process_instance_id)
            VALUES (@scopeHash, @requestHash, @operation, @createdAt, @expiresAt, false, NULL)
            ON CONFLICT (scope_hash) DO NOTHING
            """);
        command.Parameters.AddWithValue("scopeHash", record.ScopeHash);
        command.Parameters.AddWithValue("requestHash", record.RequestHash);
        command.Parameters.AddWithValue("operation", record.Operation);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("expiresAt", record.ExpiresAt);
        return await command.ExecuteNonQueryAsync() == 1;
    });

    public Task Complete(string scopeHash, Guid? processInstanceId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            UPDATE {schema}.idempotency_records SET is_completed = true, process_instance_id = @instanceId
            WHERE scope_hash = @scopeHash AND is_completed = false
            """);
        command.Parameters.AddWithValue("scopeHash", scopeHash);
        command.Parameters.AddWithValue("instanceId", (object?)processInstanceId ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The idempotency reservation was lost.");
    });

    public Task Remove(string scopeHash) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.idempotency_records WHERE scope_hash = @scopeHash AND is_completed = false");
        command.Parameters.AddWithValue("scopeHash", scopeHash);
        await command.ExecuteNonQueryAsync();
    });

    public Task DeleteExpired(DateTime utcNow) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.idempotency_records WHERE expires_at <= @utcNow");
        command.Parameters.AddWithValue("utcNow", utcNow);
        await command.ExecuteNonQueryAsync();
    });
}
