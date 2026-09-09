using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>PostgreSQL-CAS und transaktional gekoppelte Auditspur für Human Tasks.</summary>
internal sealed class PostgreSqlUserTaskLifecycleStorage(PostgreSqlSession session) : IUserTaskLifecycleStorage
{
    public Task<UserTaskWorkState?> Get(Guid userTaskId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.user_task_work_states WHERE user_task_id = @id");
        command.Parameters.AddWithValue("id", userTaskId);
        var value = await command.ExecuteScalarAsync();
        return value is string body ? StorageJson.Deserialize<UserTaskWorkState>(body) : null;
    });

    public Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> GetMany(IEnumerable<Guid> userTaskIds)
    {
        var ids = userTaskIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Task.FromResult<IReadOnlyDictionary<Guid, UserTaskWorkState>>(
                new Dictionary<Guid, UserTaskWorkState>());
        return session.RunAsync<IReadOnlyDictionary<Guid, UserTaskWorkState>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT user_task_id, body FROM {schema}.user_task_work_states WHERE user_task_id = ANY(@ids)");
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, ids);
            await using var reader = await command.ExecuteReaderAsync();
            var result = new Dictionary<Guid, UserTaskWorkState>();
            while (await reader.ReadAsync()) result[reader.GetGuid(0)] = StorageJson.Deserialize<UserTaskWorkState>(reader.GetString(1));
            return result;
        });
    }

    public Task<bool> LockTask(Guid userTaskId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT id FROM {schema}.user_task_subscriptions WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("id", userTaskId);
        return await command.ExecuteScalarAsync() is not null;
    });

    public Task<UserTaskLifecycleWriteResult> TryWrite(
        UserTaskWorkState state,
        long expectedRevision,
        UserTaskAssignmentEvent auditEvent) =>
        session.RunAsync(async (connection, transaction) =>
        {
            UserTaskLifecycleContract.Validate(state, expectedRevision, auditEvent);
            var sql = expectedRevision == 0
                ? """
                  INSERT INTO {schema}.user_task_work_states (user_task_id, revision, body)
                  SELECT @id, @revision, @body
                  WHERE EXISTS (SELECT 1 FROM {schema}.user_task_subscriptions WHERE id = @id)
                  ON CONFLICT (user_task_id) DO NOTHING
                  RETURNING body
                  """
                : """
                  UPDATE {schema}.user_task_work_states
                  SET revision = @revision, body = @body
                  WHERE user_task_id = @id AND revision = @expectedRevision
                  RETURNING body
                  """;
            await using var write = session.CreateCommand(connection, transaction, sql);
            write.Parameters.AddWithValue("id", state.UserTaskId);
            write.Parameters.AddWithValue("revision", state.Revision);
            write.Parameters.AddWithValue("body", StorageJson.Serialize(state));
            if (expectedRevision != 0) write.Parameters.AddWithValue("expectedRevision", expectedRevision);
            var written = await write.ExecuteScalarAsync();
            if (written is string body)
            {
                await InsertEvent(connection, transaction, auditEvent);
                var stored = StorageJson.Deserialize<UserTaskWorkState>(body);
                return new UserTaskLifecycleWriteResult(UserTaskLifecycleWriteStatus.Written, stored, stored.Revision);
            }

            await using var read = session.CreateCommand(connection, transaction, """
                SELECT EXISTS (SELECT 1 FROM {schema}.user_task_subscriptions WHERE id = @id),
                       (SELECT body FROM {schema}.user_task_work_states WHERE user_task_id = @id)
                """);
            read.Parameters.AddWithValue("id", state.UserTaskId);
            await using var reader = await read.ExecuteReaderAsync();
            await reader.ReadAsync();
            if (!reader.GetBoolean(0))
                return new UserTaskLifecycleWriteResult(UserTaskLifecycleWriteStatus.TaskNotFound, null, 0);
            var current = reader.IsDBNull(1) ? null : StorageJson.Deserialize<UserTaskWorkState>(reader.GetString(1));
            return new UserTaskLifecycleWriteResult(
                UserTaskLifecycleWriteStatus.RevisionConflict, current, current?.Revision ?? 0);
        });

    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEvents(Guid userTaskId) =>
        session.RunAsync<IReadOnlyList<UserTaskAssignmentEvent>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.user_task_assignment_events WHERE user_task_id = @id ORDER BY revision");
            command.Parameters.AddWithValue("id", userTaskId);
            await using var reader = await command.ExecuteReaderAsync();
            var result = new List<UserTaskAssignmentEvent>();
            while (await reader.ReadAsync()) result.Add(StorageJson.Deserialize<UserTaskAssignmentEvent>(reader.GetString(0)));
            return result;
        });

    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEventsByProcessInstance(Guid processInstanceId) =>
        session.RunAsync<IReadOnlyList<UserTaskAssignmentEvent>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.user_task_assignment_events
                WHERE process_instance_id = @processInstanceId
                ORDER BY occurred_at, user_task_id, revision, id
                """);
            command.Parameters.AddWithValue("processInstanceId", processInstanceId);
            await using var reader = await command.ExecuteReaderAsync();
            var result = new List<UserTaskAssignmentEvent>();
            while (await reader.ReadAsync()) result.Add(StorageJson.Deserialize<UserTaskAssignmentEvent>(reader.GetString(0)));
            return result;
        });

    private async Task InsertEvent(NpgsqlConnection connection, NpgsqlTransaction? transaction, UserTaskAssignmentEvent item)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.user_task_assignment_events
                (id, user_task_id, process_instance_id, revision, occurred_at, body)
            VALUES (@id, @taskId, @processInstanceId, @revision, @occurredAt, @body)
            """);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("taskId", item.UserTaskId);
        command.Parameters.AddWithValue("processInstanceId", NpgsqlDbType.Uuid,
            item.ProcessInstanceId is { } processInstanceId ? processInstanceId : DBNull.Value);
        command.Parameters.AddWithValue("revision", item.Revision);
        command.Parameters.AddWithValue("occurredAt", item.OccurredAtUtc);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(item));
        await command.ExecuteNonQueryAsync();
    }

}
