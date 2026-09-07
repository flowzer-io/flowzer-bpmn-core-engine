using Model;
using Npgsql;
using StorageSystem;
using StorageSystem.Exceptions;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Ordner des Workflow-Katalogs in PostgreSQL. Verhalten und Fehlerbilder entsprechen der
/// Dateiablage (NotFound/Conflict).
/// </summary>
internal sealed class PostgreSqlFolderStorage(PostgreSqlSession session) : IFolderStorage
{
    public Task<WorkflowFolder[]> GetAllFolders() => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.workflow_folders ORDER BY name");
        return (await PostgreSqlDefinitionStorage.ReadBodiesAsync<WorkflowFolder>(command)).ToArray();
    });

    public Task<WorkflowFolder?> GetFolder(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.workflow_folders WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null ? null : StorageJson.Deserialize<WorkflowFolder>(body);
    });

    public Task StoreFolder(WorkflowFolder folder) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.workflow_folders (id, parent_id, name, body)
            VALUES (@id, @parentId, @name, @body)
            ON CONFLICT (id) DO NOTHING
            """);
        AddFolderParameters(command, folder);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new DefinitionStorageConflictException($"Folder {folder.Id} already exists.");
        }
    });

    public Task UpdateFolder(WorkflowFolder folder) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            UPDATE {schema}.workflow_folders
            SET parent_id = @parentId, name = @name, body = @body
            WHERE id = @id
            """);
        AddFolderParameters(command, folder);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {folder.Id}");
        }
    });

    public Task DeleteFolder(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "DELETE FROM {schema}.workflow_folders WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {id}");
        }
    });

    private static void AddFolderParameters(NpgsqlCommand command, WorkflowFolder folder)
    {
        command.Parameters.AddWithValue("id", folder.Id);
        command.Parameters.AddWithValue("parentId", folder.ParentId.HasValue ? folder.ParentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("name", folder.Name);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(folder));
    }
}
