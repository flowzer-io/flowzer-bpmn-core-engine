using Model;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>PostgreSQL-Persistenz für immutable Terminbindung und Scheduler-CAS.</summary>
internal sealed class PostgreSqlUserTaskDeadlineStorage(PostgreSqlSession session) : IUserTaskDeadlineStorage
{
    public Task<UserTaskDeadline> AddIfAbsent(UserTaskDeadline deadline) => session.RunAsync(async (connection, transaction) =>
    {
        UserTaskDeadlineContract.Validate(deadline);
        await using var insert = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.user_task_deadlines (user_task_id, revision, next_check_at, body)
            SELECT @id, @revision, @nextCheckAt, @body
            WHERE EXISTS (SELECT 1 FROM {schema}.user_task_subscriptions WHERE id = @id)
            ON CONFLICT (user_task_id) DO NOTHING
            RETURNING body
            """);
        AddDeadlineParameters(insert, deadline);
        var inserted = await insert.ExecuteScalarAsync();
        if (inserted is string body) return StorageJson.Deserialize<UserTaskDeadline>(body);

        await using var read = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.user_task_deadlines WHERE user_task_id = @id");
        read.Parameters.AddWithValue("id", deadline.UserTaskId);
        var existing = await read.ExecuteScalarAsync();
        return existing is string existingBody
            ? StorageJson.Deserialize<UserTaskDeadline>(existingBody)
            : throw new FileNotFoundException("The user task for the deadline does not exist.");
    });

    public Task<UserTaskDeadline?> Get(Guid userTaskId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.user_task_deadlines WHERE user_task_id = @id");
        command.Parameters.AddWithValue("id", userTaskId);
        var value = await command.ExecuteScalarAsync();
        return value is string body ? StorageJson.Deserialize<UserTaskDeadline>(body) : null;
    });

    public Task<IReadOnlyList<UserTaskDeadline>> GetDueCandidates(DateTimeOffset nowUtc, int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return session.RunAsync<IReadOnlyList<UserTaskDeadline>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.user_task_deadlines
                WHERE next_check_at IS NOT NULL AND next_check_at <= @now
                ORDER BY next_check_at, user_task_id
                LIMIT @limit
                """);
            command.Parameters.AddWithValue("now", nowUtc);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            var result = new List<UserTaskDeadline>();
            while (await reader.ReadAsync()) result.Add(StorageJson.Deserialize<UserTaskDeadline>(reader.GetString(0)));
            return result;
        });
    }

    public Task<bool> TryAdvance(UserTaskDeadline deadline, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            UserTaskDeadlineContract.Validate(deadline);
            if (deadline.Revision != checked(expectedRevision + 1))
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            await using var command = session.CreateCommand(connection, transaction, """
                UPDATE {schema}.user_task_deadlines
                SET revision = @revision, next_check_at = @nextCheckAt, body = @body
                WHERE user_task_id = @id AND revision = @expectedRevision
                """);
            AddDeadlineParameters(command, deadline);
            command.Parameters.AddWithValue("expectedRevision", expectedRevision);
            return await command.ExecuteNonQueryAsync() == 1;
        });

    private static void AddDeadlineParameters(Npgsql.NpgsqlCommand command, UserTaskDeadline deadline)
    {
        command.Parameters.AddWithValue("id", deadline.UserTaskId);
        command.Parameters.AddWithValue("revision", deadline.Revision);
        var next = command.Parameters.AddWithValue("nextCheckAt", (object?)deadline.NextCheckAtUtc ?? DBNull.Value);
        next.NpgsqlDbType = NpgsqlDbType.TimestampTz;
        command.Parameters.AddWithValue("body", StorageJson.Serialize(deadline));
    }
}
