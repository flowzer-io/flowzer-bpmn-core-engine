using System.Reflection;
using Npgsql;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Fuehrt die eingebetteten SQL-Migrationen (<c>Migrations/NNN_name.sql</c>) genau einmal aus
/// und protokolliert sie in <c>{schema}.schema_migrations</c>. Ein Advisory-Lock verhindert,
/// dass zwei Migrationslaeufe gleichzeitig arbeiten. Das Schema wird angelegt, falls es fehlt.
/// </summary>
public static class PostgreSqlMigrator
{
    private const long AdvisoryLockKey = 0x464C4F575A4552; // "FLOWZER"

    public static async Task<IReadOnlyList<int>> ApplyAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        var migrations = LoadEmbeddedMigrations();
        var applied = new List<int>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);
        await ExecuteAsync(connection, transaction, $"CREATE SCHEMA IF NOT EXISTS {Quote(schema)}", cancellationToken);
        await ExecuteAsync(connection, transaction,
            $"CREATE TABLE IF NOT EXISTS {Quote(schema)}.schema_migrations (version integer PRIMARY KEY, name text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())",
            cancellationToken);

        var appliedVersions = new HashSet<int>();
        await using (var command = new NpgsqlCommand($"SELECT version FROM {Quote(schema)}.schema_migrations", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                appliedVersions.Add(reader.GetInt32(0));
            }
        }

        foreach (var (version, name, sql) in migrations)
        {
            if (appliedVersions.Contains(version))
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, sql.Replace("{schema}", Quote(schema), StringComparison.Ordinal), cancellationToken);
            await using var insert = new NpgsqlCommand($"INSERT INTO {Quote(schema)}.schema_migrations (version, name) VALUES (@version, @name)", connection, transaction);
            insert.Parameters.AddWithValue("version", version);
            insert.Parameters.AddWithValue("name", name);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            applied.Add(version);
        }

        await transaction.CommitAsync(cancellationToken);
        return applied;
    }

    private static IReadOnlyList<(int Version, string Name, string Sql)> LoadEmbeddedMigrations()
    {
        var assembly = typeof(PostgreSqlMigrator).Assembly;
        var migrations = new List<(int, string, string)>();
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = resourceName.Split('.')[^2];
            var separatorIndex = fileName.IndexOf('_');
            if (separatorIndex <= 0 || !int.TryParse(fileName[..separatorIndex], out var version))
            {
                throw new InvalidOperationException($"Migration resource '{resourceName}' must be named NNN_name.sql.");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            migrations.Add((version, fileName, reader.ReadToEnd()));
        }

        return migrations.OrderBy(migration => migration.Item1).ToList();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
