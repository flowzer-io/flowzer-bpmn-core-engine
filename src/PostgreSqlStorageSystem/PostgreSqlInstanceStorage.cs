using StorageSystem;

namespace PostgreSqlStorageSystem;

internal sealed class PostgreSqlInstanceStorage(PostgreSqlSession session) : IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.instances WHERE instance_id = @id");
        command.Parameters.AddWithValue("id", processInstanceId);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new FileNotFoundException($"Process instance {processInstanceId} was not found.")
            : StorageJson.Deserialize<ProcessInstanceInfo>(body);
    });

    public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.instances (instance_id, meta_definition_id, is_finished, body)
            VALUES (@id, @metaDefinitionId, @isFinished, @body)
            ON CONFLICT (instance_id) DO UPDATE SET
                meta_definition_id = EXCLUDED.meta_definition_id,
                is_finished = EXCLUDED.is_finished,
                body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", processInstanceInfo.InstanceId);
        command.Parameters.AddWithValue("metaDefinitionId", processInstanceInfo.metaDefinitionId);
        command.Parameters.AddWithValue("isFinished", processInstanceInfo.IsFinished);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(processInstanceInfo));
        await command.ExecuteNonQueryAsync();
    });

    public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Query("SELECT body FROM {schema}.instances WHERE is_finished = false");

    public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Query("SELECT body FROM {schema}.instances");

    private Task<IEnumerable<ProcessInstanceInfo>> Query(string sql) => session.RunAsync<IEnumerable<ProcessInstanceInfo>>(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, sql);
        return await PostgreSqlDefinitionStorage.ReadBodiesAsync<ProcessInstanceInfo>(command);
    });
}
