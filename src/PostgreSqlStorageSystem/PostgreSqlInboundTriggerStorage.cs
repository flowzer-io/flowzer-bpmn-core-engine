using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Auslöser in PostgreSQL. Zählerstand und letzter Fehler stehen in eigenen Spalten, nicht im
/// JSON-Körper: Nur so lässt sich ein Aufruf in einem Statement zählen, ohne eine gleichzeitige
/// Änderung der Verwaltung zu überschreiben. Beim Lesen werden die Spalten wieder in das Objekt
/// gehoben, damit Aufrufer nur mit <see cref="InboundTrigger"/> arbeiten.
/// </summary>
internal sealed class PostgreSqlInboundTriggerStorage(PostgreSqlSession session) : IInboundTriggerStorage
{
    private const string Columns =
        "body, enabled, created_at, last_used_at, use_count, last_failure_at, last_failure_reason";

    public Task<IReadOnlyList<InboundTrigger>> GetAll() => session.RunAsync<IReadOnlyList<InboundTrigger>>(
        async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                $"SELECT {Columns} FROM {{schema}}.inbound_triggers ORDER BY created_at, id");
            return await Read(command);
        });

    public Task<InboundTrigger?> Get(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {Columns} FROM {{schema}}.inbound_triggers WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return (await Read(command)).FirstOrDefault();
    });

    public Task<InboundTrigger?> GetByKey(string key) => session.RunAsync(async (connection, transaction) =>
    {
        // Bewusst ohne Filter auf `enabled`: Ein abgeschalteter Auslöser muss von außen genauso
        // aussehen wie ein unbekannter, und diese Entscheidung trifft der Aufrufer, nicht die Ablage.
        await using var command = session.CreateCommand(connection, transaction,
            $"SELECT {Columns} FROM {{schema}}.inbound_triggers WHERE trigger_key = @key");
        command.Parameters.AddWithValue("key", key);
        return (await Read(command)).FirstOrDefault();
    });

    public Task Save(InboundTrigger trigger) => session.RunAsync(async (connection, transaction) =>
    {
        // Zähler und Fehlerfelder stehen nicht im UPDATE-Zweig: Ein Umbenennen darf den
        // Nutzungsstand nicht auf den Stand zurücksetzen, den der Aufrufer zufällig geladen hatte.
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.inbound_triggers
                (id, trigger_key, enabled, created_at, last_used_at, use_count, last_failure_at,
                 last_failure_reason, body)
            VALUES (@id, @key, @enabled, @createdAt, NULL, 0, NULL, NULL, @body)
            ON CONFLICT (id) DO UPDATE SET
                trigger_key = EXCLUDED.trigger_key,
                enabled = EXCLUDED.enabled,
                body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", trigger.Id);
        command.Parameters.AddWithValue("key", trigger.Key);
        command.Parameters.AddWithValue("enabled", trigger.Enabled);
        AddTimestamp(command, "createdAt", trigger.CreatedAt);
        command.Parameters.AddWithValue("body", StorageJson.SerializeConcrete(trigger));
        await command.ExecuteNonQueryAsync();
    });

    public Task<bool> Remove(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.inbound_triggers WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync() > 0;
    });

    public Task RecordUse(Guid id, DateTime usedAt) => session.RunAsync(async (connection, transaction) =>
    {
        // `use_count + 1` in der Datenbank, nicht im Anwendungsspeicher: Zwei gleichzeitige
        // Aufrufe desselben Auslösers sind der Normalfall und dürften sonst als einer zählen.
        await using var command = session.CreateCommand(connection, transaction, """
            UPDATE {schema}.inbound_triggers
            SET use_count = use_count + 1,
                last_used_at = @usedAt
            WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        AddTimestamp(command, "usedAt", usedAt);
        await command.ExecuteNonQueryAsync();
    });

    public Task RecordFailure(Guid id, DateTime failedAt, string reason) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                UPDATE {schema}.inbound_triggers
                SET last_failure_at = @failedAt,
                    last_failure_reason = @reason
                WHERE id = @id
                """);
            command.Parameters.AddWithValue("id", id);
            AddTimestamp(command, "failedAt", failedAt);
            command.Parameters.AddWithValue("reason", reason).NpgsqlDbType = NpgsqlDbType.Text;
            await command.ExecuteNonQueryAsync();
        });

    /// <summary>Nutzungsstand und Abschaltung kommen aus den Spalten, nicht aus dem Körper.</summary>
    private static async Task<List<InboundTrigger>> Read(NpgsqlCommand command)
    {
        var triggers = new List<InboundTrigger>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var trigger = StorageJson.DeserializeConcrete<InboundTrigger>(reader.GetString(0));
            trigger.Enabled = reader.GetBoolean(1);
            trigger.CreatedAt = reader.GetDateTime(2);
            trigger.LastUsedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            trigger.UseCount = reader.GetInt64(4);
            trigger.LastFailureAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
            trigger.LastFailureReason = reader.IsDBNull(6) ? null : reader.GetString(6);
            triggers.Add(trigger);
        }

        return triggers;
    }

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTime? value)
    {
        var parameter = command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
    }
}
