using Model;
using Npgsql;
using StorageSystem;
using StorageSystem.Exceptions;
using Version = Model.Version;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Definitionen, BPMN-XML und Katalogeintraege (Meta-Definitionen) in PostgreSQL.
/// Verhalten und Fehlerbilder entsprechen der Dateiablage (NotFound/Conflict).
/// </summary>
internal sealed class PostgreSqlDefinitionStorage(PostgreSqlSession session) : IDefinitionStorage
{
    public Task StoreBinary(Guid guid, string data) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "INSERT INTO {schema}.definition_binaries (id, xml) VALUES (@id, @xml) ON CONFLICT (id) DO UPDATE SET xml = EXCLUDED.xml");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("xml", data);
        await command.ExecuteNonQueryAsync();
    });

    public Task<string> GetBinary(Guid guid) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT xml FROM {schema}.definition_binaries WHERE id = @id");
        command.Parameters.AddWithValue("id", guid);
        var result = await command.ExecuteScalarAsync() as string;
        return result ?? throw new FileNotFoundException($"Binary definition {guid} was not found.");
    });

    public Task DeleteBinary(Guid guid) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "DELETE FROM {schema}.definition_binaries WHERE id = @id");
        command.Parameters.AddWithValue("id", guid);
        await command.ExecuteNonQueryAsync();
    });

    public Task<Guid[]> GetAllBinaryDefinitions() => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT id FROM {schema}.definition_binaries");
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.ToArray();
    });

    public Task<BpmnDefinition[]> GetAllDefinitions() => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.definitions");
        return (await ReadBodiesAsync<BpmnDefinition>(command)).ToArray();
    });

    public Task StoreDefinition(BpmnDefinition definition) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.definitions (id, definition_id, is_active, version_major, version_minor, saved_on, body)
            VALUES (@id, @definitionId, @isActive, @major, @minor, @savedOn, @body)
            ON CONFLICT (id) DO UPDATE SET
                definition_id = EXCLUDED.definition_id,
                is_active = EXCLUDED.is_active,
                version_major = EXCLUDED.version_major,
                version_minor = EXCLUDED.version_minor,
                saved_on = EXCLUDED.saved_on,
                body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", definition.Id);
        command.Parameters.AddWithValue("definitionId", definition.DefinitionId);
        command.Parameters.AddWithValue("isActive", definition.IsActive);
        command.Parameters.AddWithValue("major", definition.Version.Major);
        command.Parameters.AddWithValue("minor", definition.Version.Minor);
        command.Parameters.AddWithValue("savedOn", DateTime.SpecifyKind(definition.SavedOn, DateTimeKind.Utc));
        command.Parameters.AddWithValue("body", StorageJson.Serialize(definition));
        await command.ExecuteNonQueryAsync();
    });

    public Task DeleteDefinition(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "DELETE FROM {schema}.definitions WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    });

    public async Task<Version?> GetMaxVersionId(string modelId)
    {
        var definitions = await GetDefinitionsOf(modelId);
        return definitions.Length == 0 ? null : definitions.Max(definition => definition.Version);
    }

    public Task<BpmnDefinition> GetDefinitionById(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.definitions WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new DefinitionStorageNotFoundException($"No definition found for definitionId {id}")
            : StorageJson.Deserialize<BpmnDefinition>(body);
    });

    public async Task<BpmnDefinition> GetLatestDefinition(string definitionId)
    {
        var definitions = await GetDefinitionsOf(definitionId);
        return definitions.MaxBy(definition => definition.Version)
               ?? throw new DefinitionStorageNotFoundException($"No definition found for definitionId {definitionId}");
    }

    public async Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
    {
        var definitions = await GetDefinitionsOf(definitionDefinitionId);
        return definitions.SingleOrDefault(definition => definition.IsActive);
    }

    public async Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
    {
        var metaDefinitions = await session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.meta_definitions ORDER BY definition_id");
            return await ReadBodiesAsync<ExtendedBpmnMetaDefinition>(command);
        });
        var definitionsByCatalogId = (await GetAllDefinitions())
            .GroupBy(definition => definition.DefinitionId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var metaDefinition in metaDefinitions)
        {
            if (!definitionsByCatalogId.TryGetValue(metaDefinition.DefinitionId, out var definitions))
            {
                continue;
            }

            var latest = definitions.MaxBy(definition => definition.Version)!;
            metaDefinition.LatestVersion = latest.Version;
            metaDefinition.LatestVersionDateTime = latest.SavedOn;

            var deployed = definitions.SingleOrDefault(definition => definition.IsActive);
            if (deployed is not null)
            {
                metaDefinition.DeployedId = deployed.Id;
                metaDefinition.DeployedVersion = deployed.Version;
                metaDefinition.DeployedVersionDateTime = deployed.SavedOn;
            }
        }

        return metaDefinitions.ToArray();
    }

    public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "INSERT INTO {schema}.meta_definitions (definition_id, body) VALUES (@definitionId, @body) ON CONFLICT (definition_id) DO NOTHING");
        command.Parameters.AddWithValue("definitionId", metaDefinition.DefinitionId);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(metaDefinition));
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new DefinitionStorageConflictException($"Meta definition already exists for definitionId {metaDefinition.DefinitionId}");
        }
    });

    public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "UPDATE {schema}.meta_definitions SET body = @body WHERE definition_id = @definitionId");
        command.Parameters.AddWithValue("definitionId", metaDefinition.DefinitionId);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(metaDefinition));
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {metaDefinition.DefinitionId}");
        }
    });

    public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.meta_definitions WHERE definition_id = @definitionId");
        command.Parameters.AddWithValue("definitionId", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {id}")
            : StorageJson.Deserialize<BpmnMetaDefinition>(body);
    });

    private Task<BpmnDefinition[]> GetDefinitionsOf(string definitionId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.definitions WHERE definition_id = @definitionId");
        command.Parameters.AddWithValue("definitionId", definitionId);
        return (await ReadBodiesAsync<BpmnDefinition>(command)).ToArray();
    });

    internal static async Task<List<T>> ReadBodiesAsync<T>(NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<T>();
        while (await reader.ReadAsync())
        {
            items.Add(StorageJson.Deserialize<T>(reader.GetString(0)));
        }

        return items;
    }
}
