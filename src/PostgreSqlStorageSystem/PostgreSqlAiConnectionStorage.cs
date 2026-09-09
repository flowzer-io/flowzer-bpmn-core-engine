using Model;
using Npgsql;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-CAS fuer KI-Verbindungsmetadaten. Ein transaktionaler Advisory-Lock auf dem
/// normalisierten Namen schliesst auch konkurrierende Umbenennungen verschiedener IDs ein.
/// </summary>
internal sealed class PostgreSqlAiConnectionStorage(PostgreSqlSession session) : IAiConnectionStorage
{
    public Task<IReadOnlyList<AiConnection>> List() => session.RunAsync<IReadOnlyList<AiConnection>>(
        async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.ai_connections ORDER BY lower(name), id");
            return await Read(command);
        });

    public Task<AiConnection?> Get(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateId(id);
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.ai_connections WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null ? null : StorageJson.Deserialize<AiConnection>(body);
    });

    public Task<AiConnection?> Get(Guid id, long revision) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateId(id);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.ai_connection_revisions WHERE id = @id AND revision = @revision");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("revision", revision);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null ? null : StorageJson.Deserialize<AiConnection>(body);
    });

    public Task<AiConnectionWriteResult> TryCreate(AiConnection item) =>
        session.RunAsync(async (connection, transaction) =>
        {
            Validate(item, expectedRevision: 0);
            await LockName(connection, transaction, item.Name);
            await using var command = session.CreateCommand(connection, transaction, """
                INSERT INTO {schema}.ai_connections
                    (id, name, revision, updated_at, secret_reference, body)
                VALUES (@id, @name, @revision, @updatedAt, @secretReference, @body)
                ON CONFLICT DO NOTHING
                RETURNING body
                """);
            AddParameters(command, item);
            var body = await command.ExecuteScalarAsync() as string;
            if (body is not null)
            {
                await StoreRevision(connection, transaction, item);
                return new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Written,
                    StorageJson.Deserialize<AiConnection>(body),
                    item.Revision);
            }

            var current = await ReadCurrent(connection, transaction, item.Id);
            return new AiConnectionWriteResult(
                AiConnectionWriteStatus.Conflict,
                current,
                current?.Revision ?? 0);
        });

    public Task<AiConnectionWriteResult> TryUpdate(AiConnection item, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            Validate(item, expectedRevision);
            await LockName(connection, transaction, item.Name);
            await using var command = session.CreateCommand(connection, transaction, """
                UPDATE {schema}.ai_connections AS target
                SET name = @name,
                    revision = @revision,
                    updated_at = @updatedAt,
                    secret_reference = @secretReference,
                    body = @body
                WHERE target.id = @id
                  AND target.revision = @expectedRevision
                  AND NOT EXISTS (
                      SELECT 1 FROM {schema}.ai_connections AS duplicate
                      WHERE duplicate.id <> @id AND lower(duplicate.name) = lower(@name)
                  )
                RETURNING body
                """);
            AddParameters(command, item);
            command.Parameters.AddWithValue("expectedRevision", expectedRevision);
            var body = await command.ExecuteScalarAsync() as string;
            if (body is not null)
            {
                await StoreRevision(connection, transaction, item);
                return new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Written,
                    StorageJson.Deserialize<AiConnection>(body),
                    item.Revision);
            }

            var current = await ReadCurrent(connection, transaction, item.Id);
            return current is null
                ? new AiConnectionWriteResult(AiConnectionWriteStatus.NotFound, null, 0)
                : new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Conflict,
                    current,
                    current.Revision);
        });

    private async Task LockName(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string name)
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT pg_advisory_xact_lock(hashtextextended(lower(@name), 0))");
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<AiConnection?> ReadCurrent(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id)
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.ai_connections WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null ? null : StorageJson.Deserialize<AiConnection>(body);
    }

    private async Task StoreRevision(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AiConnection item)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.ai_connection_revisions (id, revision, body)
            VALUES (@id, @revision, @body)
            ON CONFLICT (id, revision) DO NOTHING
            RETURNING body
            """);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("revision", item.Revision);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(item));
        var inserted = await command.ExecuteScalarAsync() as string;
        if (inserted is not null)
            return;

        await using var existingCommand = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.ai_connection_revisions WHERE id = @id AND revision = @revision");
        existingCommand.Parameters.AddWithValue("id", item.Id);
        existingCommand.Parameters.AddWithValue("revision", item.Revision);
        var existing = await existingCommand.ExecuteScalarAsync() as string;
        if (existing is null || StorageJson.Deserialize<AiConnection>(existing) != item)
            throw new InvalidDataException("An immutable AI connection revision already contains different data.");
    }

    private static async Task<IReadOnlyList<AiConnection>> Read(NpgsqlCommand command)
    {
        var result = new List<AiConnection>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(StorageJson.Deserialize<AiConnection>(reader.GetString(0)));
        }
        return result;
    }

    private static void AddParameters(NpgsqlCommand command, AiConnection item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("name", item.Name);
        command.Parameters.AddWithValue("revision", item.Revision);
        command.Parameters.AddWithValue("updatedAt", item.UpdatedAtUtc);
        command.Parameters.AddWithValue("secretReference", item.SecretReference);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(item));
    }

    private static void Validate(AiConnection item, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateId(item.Id);
        if (expectedRevision < 0 || item.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.DefaultModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.SecretReference);
        if (item.UpdatedByUserId == Guid.Empty)
            throw new ArgumentException("Updating user ID is required.", nameof(item));
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("AI connection ID is required.", nameof(id));
    }
}
