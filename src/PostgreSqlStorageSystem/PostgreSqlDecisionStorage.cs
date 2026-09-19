using Model;
using Npgsql;
using StorageSystem;
using StorageSystem.Exceptions;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-Ablage der Entscheidungsdateien: Katalogkopf und unveraenderliche XML-Staende.
/// </summary>
internal sealed class PostgreSqlDecisionStorage(PostgreSqlSession session) : IDecisionStorage
{
    public Task<IReadOnlyList<DecisionDefinition>> GetDefinitions() =>
        session.RunAsync<IReadOnlyList<DecisionDefinition>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.decision_definitions ORDER BY decision_definition_id");
            return await ReadAsync<DecisionDefinition>(command);
        });

    public Task<DecisionDefinition?> GetDefinition(string decisionDefinitionId) =>
        session.RunAsync(async (connection, transaction) =>
        {
            DefinitionIdRules.EnsureValid(decisionDefinitionId);
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.decision_definitions WHERE decision_definition_id = @id");
            command.Parameters.AddWithValue("id", decisionDefinitionId);
            return await command.ExecuteScalarAsync() is string body
                ? StorageJson.DeserializeConcrete<DecisionDefinition>(body)
                : null;
        });

    public Task SaveDefinition(DecisionDefinition definition) => session.RunAsync(async (connection, transaction) =>
    {
        ArgumentNullException.ThrowIfNull(definition);
        DefinitionIdRules.EnsureValid(definition.DecisionDefinitionId);
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.decision_definitions (decision_definition_id, name, body)
            VALUES (@id, @name, @body)
            ON CONFLICT (decision_definition_id) DO UPDATE SET name = EXCLUDED.name, body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", definition.DecisionDefinitionId);
        command.Parameters.AddWithValue("name", definition.Name);
        command.Parameters.AddWithValue("body", StorageJson.SerializeConcrete(definition));
        await command.ExecuteNonQueryAsync();
    });

    public Task DeleteDefinition(string decisionDefinitionId) => session.RunAsync(async (connection, transaction) =>
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);

        // Der Fremdschluessel steht auf RESTRICT; die Staende gehen deshalb ausdruecklich
        // voran — in derselben Transaktion wie der Kopf.
        await using var deleteVersions = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.decision_definition_versions WHERE decision_definition_id = @id");
        deleteVersions.Parameters.AddWithValue("id", decisionDefinitionId);
        await deleteVersions.ExecuteNonQueryAsync();

        await using var deleteDefinition = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.decision_definitions WHERE decision_definition_id = @id");
        deleteDefinition.Parameters.AddWithValue("id", decisionDefinitionId);
        await deleteDefinition.ExecuteNonQueryAsync();
    });

    public Task<IReadOnlyList<DecisionDefinitionVersion>> GetVersions(string decisionDefinitionId) =>
        session.RunAsync<IReadOnlyList<DecisionDefinitionVersion>>(async (connection, transaction) =>
        {
            DefinitionIdRules.EnsureValid(decisionDefinitionId);
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.decision_definition_versions
                WHERE decision_definition_id = @id
                ORDER BY version
                """);
            command.Parameters.AddWithValue("id", decisionDefinitionId);
            return await ReadAsync<DecisionDefinitionVersion>(command);
        });

    public Task<DecisionDefinitionVersion?> GetVersion(string decisionDefinitionId, int version) =>
        session.RunAsync(async (connection, transaction) =>
        {
            DefinitionIdRules.EnsureValid(decisionDefinitionId);
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.decision_definition_versions
                WHERE decision_definition_id = @id AND version = @version
                """);
            command.Parameters.AddWithValue("id", decisionDefinitionId);
            command.Parameters.AddWithValue("version", version);
            return await command.ExecuteScalarAsync() is string body
                ? StorageJson.DeserializeConcrete<DecisionDefinitionVersion>(body)
                : null;
        });

    /// <summary>Die Datenbank beantwortet den juengsten Stand ohne alle Staende zu lesen.</summary>
    public Task<DecisionDefinitionVersion?> GetLatestVersion(string decisionDefinitionId) =>
        session.RunAsync(async (connection, transaction) =>
        {
            DefinitionIdRules.EnsureValid(decisionDefinitionId);
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.decision_definition_versions
                WHERE decision_definition_id = @id
                ORDER BY version DESC
                LIMIT 1
                """);
            command.Parameters.AddWithValue("id", decisionDefinitionId);
            return await command.ExecuteScalarAsync() is string body
                ? StorageJson.DeserializeConcrete<DecisionDefinitionVersion>(body)
                : null;
        });

    public Task SaveVersion(DecisionDefinitionVersion version) => session.RunAsync(async (connection, transaction) =>
    {
        ArgumentNullException.ThrowIfNull(version);
        DefinitionIdRules.EnsureValid(version.DecisionDefinitionId);
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.decision_definition_versions
                (decision_definition_id, version, deployed_at, body)
            VALUES (@id, @version, @deployedAt, @body)
            """);
        command.Parameters.AddWithValue("id", version.DecisionDefinitionId);
        command.Parameters.AddWithValue("version", version.Version);
        command.Parameters.AddWithValue("deployedAt", version.DeployedAt);
        command.Parameters.AddWithValue("body", StorageJson.SerializeConcrete(version));
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DefinitionStorageConflictException(
                $"Version {version.Version} of the decision definition '{version.DecisionDefinitionId}' already exists.");
        }
    });

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<T>();
        while (await reader.ReadAsync())
        {
            items.Add(StorageJson.DeserializeConcrete<T>(reader.GetString(0)));
        }

        return items;
    }
}
