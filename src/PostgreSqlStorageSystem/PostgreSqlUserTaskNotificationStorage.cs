using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>PostgreSQL-Outbox für taskgebundene, deduplizierte In-App-Meldungen.</summary>
internal sealed class PostgreSqlUserTaskNotificationStorage(PostgreSqlSession session) : IUserTaskNotificationStorage
{
    public Task<UserTaskNotificationWriteResult> TryAdd(UserTaskNotification notification) =>
        session.RunAsync<UserTaskNotificationWriteResult>(async (connection, transaction) =>
        {
            UserTaskNotificationContract.Validate(notification);
            await using var insert = session.CreateCommand(connection, transaction, """
                INSERT INTO {schema}.user_task_notifications
                    (id, user_task_id, kind, occurred_at, deduplication_key, body)
                SELECT @id, @taskId, @kind, @occurredAt, @deduplicationKey, @body
                WHERE EXISTS (SELECT 1 FROM {schema}.user_task_subscriptions WHERE id = @taskId)
                ON CONFLICT (deduplication_key) DO NOTHING
                RETURNING body
                """);
            AddNotificationParameters(insert, notification);
            var written = await insert.ExecuteScalarAsync();
            if (written is string body)
                return new(true, StorageJson.Deserialize<UserTaskNotification>(body));

            await using var read = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.user_task_notifications WHERE deduplication_key = @key");
            read.Parameters.AddWithValue("key", notification.DeduplicationKey);
            var existing = await read.ExecuteScalarAsync();
            return new(false, existing is string existingBody
                ? StorageJson.Deserialize<UserTaskNotification>(existingBody)
                : null);
        });

    public Task<UserTaskNotification?> Get(Guid notificationId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.user_task_notifications WHERE id = @id");
        command.Parameters.AddWithValue("id", notificationId);
        var value = await command.ExecuteScalarAsync();
        return value is string body ? StorageJson.Deserialize<UserTaskNotification>(body) : null;
    });

    public Task<IReadOnlyList<UserTaskNotificationView>> GetForTasks(
        IEnumerable<Guid> userTaskIds, string ownerKey, DateTimeOffset? beforeUtc, int limit, bool unreadOnly)
    {
        UserTaskNotificationContract.ValidateOwner(ownerKey);
        var ids = userTaskIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult<IReadOnlyList<UserTaskNotificationView>>([]);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return session.RunAsync<IReadOnlyList<UserTaskNotificationView>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT n.body, r.read_at
                FROM {schema}.user_task_notifications n
                LEFT JOIN {schema}.user_task_notification_reads r
                  ON r.notification_id = n.id AND r.owner_key = @ownerKey
                WHERE n.user_task_id = ANY(@ids)
                  AND (@before IS NULL OR n.occurred_at < @before)
                  AND (@unreadOnly = FALSE OR r.notification_id IS NULL)
                ORDER BY n.occurred_at DESC, n.id DESC
                LIMIT @limit
                """);
            command.Parameters.AddWithValue("ownerKey", ownerKey);
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, ids);
            var before = command.Parameters.AddWithValue("before", (object?)beforeUtc ?? DBNull.Value);
            before.NpgsqlDbType = NpgsqlDbType.TimestampTz;
            command.Parameters.AddWithValue("unreadOnly", unreadOnly);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            var result = new List<UserTaskNotificationView>();
            while (await reader.ReadAsync())
                result.Add(new(StorageJson.Deserialize<UserTaskNotification>(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1)));
            return result;
        });
    }

    public Task<bool> MarkRead(Guid notificationId, string ownerKey, DateTimeOffset readAtUtc) =>
        session.RunAsync(async (connection, transaction) =>
        {
            UserTaskNotificationContract.ValidateOwner(ownerKey);
            await using var command = session.CreateCommand(connection, transaction, """
                INSERT INTO {schema}.user_task_notification_reads (notification_id, owner_key, read_at)
                SELECT @id, @ownerKey, @readAt
                WHERE EXISTS (SELECT 1 FROM {schema}.user_task_notifications WHERE id = @id)
                ON CONFLICT (notification_id, owner_key) DO NOTHING
                """);
            command.Parameters.AddWithValue("id", notificationId);
            command.Parameters.AddWithValue("ownerKey", ownerKey);
            command.Parameters.AddWithValue("readAt", readAtUtc);
            if (await command.ExecuteNonQueryAsync() == 1) return true;

            await using var exists = session.CreateCommand(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM {schema}.user_task_notifications WHERE id = @id)");
            exists.Parameters.AddWithValue("id", notificationId);
            return await exists.ExecuteScalarAsync() is true;
        });

    private static void AddNotificationParameters(NpgsqlCommand command, UserTaskNotification item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("taskId", item.UserTaskId);
        command.Parameters.AddWithValue("kind", item.Kind);
        command.Parameters.AddWithValue("occurredAt", item.OccurredAtUtc);
        command.Parameters.AddWithValue("deduplicationKey", item.DeduplicationKey);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(item));
    }
}
