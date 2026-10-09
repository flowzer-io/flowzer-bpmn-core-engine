using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Model;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>Kurze Person-/Versionssperre für Ausgabe; atomarer Hashverbrauch für Einlösung.</summary>
internal sealed class PostgreSqlStartFormEmbedGrantStorage(PostgreSqlSession session) : IStartFormEmbedGrantStorage
{
    public Task Create(StartFormEmbedGrant grant, DateTimeOffset utcNow) => session.RunAsync(async (connection, transaction) =>
    {
        // Kein vorhandener Task kann diese Ausgabe sperren. Eine transaktionsgebundene
        // Advisory-Sperre schützt die Obergrenze auch zwischen unabhängigen API-Hosts.
        // Die nachfolgende Abfrage erhält nach dem Warten einen frischen READ-COMMITTED-Stand.
        var material = Encoding.UTF8.GetBytes($"start-form\0{session.Schema}\0{grant.OwnerKey}\0{grant.DefinitionId:D}");
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(material));
        await using (var gate = session.CreateCommand(connection, transaction, "SELECT pg_advisory_xact_lock(@key)"))
        {
            gate.Parameters.AddWithValue("key", lockKey);
            await gate.ExecuteNonQueryAsync();
        }
        await using var command = session.CreateCommand(connection, transaction, """
            DELETE FROM {schema}.start_form_embed_grants WHERE secret_hash IN (
                SELECT secret_hash FROM {schema}.start_form_embed_grants
                WHERE owner_key = @owner AND definition_id = @definition
                ORDER BY expires_at DESC, secret_hash OFFSET 3
            );
            INSERT INTO {schema}.start_form_embed_grants (secret_hash, owner_key, definition_id, expires_at, body)
            VALUES (@hash, @owner, @definition, @expiresAt, @body);
            """);
        command.Parameters.AddWithValue("hash", grant.SecretHash);
        command.Parameters.AddWithValue("owner", grant.OwnerKey);
        command.Parameters.AddWithValue("definition", grant.DefinitionId);
        command.Parameters.AddWithValue("expiresAt", grant.ExpiresAtUtc);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(grant));
        await command.ExecuteNonQueryAsync();
    });

    public Task<StartFormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.start_form_embed_grants WHERE secret_hash = @hash AND expires_at > @now");
            command.Parameters.AddWithValue("hash", secretHash);
            command.Parameters.AddWithValue("now", utcNow);
            var body = await command.ExecuteScalarAsync(cancellationToken);
            return body is string json ? StorageJson.Deserialize<StartFormEmbedGrant>(json) : null;
        });

    public Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                DELETE FROM {schema}.start_form_embed_grants WHERE secret_hash IN (
                    SELECT secret_hash FROM {schema}.start_form_embed_grants WHERE expires_at <= @now
                    ORDER BY expires_at LIMIT 200 FOR UPDATE SKIP LOCKED
                )
                """);
            command.Parameters.AddWithValue("now", utcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });

    public Task<StartFormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.start_form_embed_grants WHERE secret_hash = @hash RETURNING body, expires_at");
        command.Parameters.AddWithValue("hash", secretHash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetFieldValue<DateTimeOffset>(1) <= utcNow) return null;
        return StorageJson.Deserialize<StartFormEmbedGrant>(reader.GetString(0));
    });
}
