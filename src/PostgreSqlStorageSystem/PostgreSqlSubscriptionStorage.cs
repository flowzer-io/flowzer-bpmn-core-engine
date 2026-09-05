using Model;
using Npgsql;
using StorageSystem;
using StorageSystem.Exceptions;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Message-, Signal-, User-Task- und Timer-Subscriptions in PostgreSQL. Anders als die
/// Dateiablage haelt diese Ablage mehrere Message-Subscriptions je Instanz auseinander.
/// </summary>
internal sealed class PostgreSqlSubscriptionStorage(PostgreSqlSession session, IDefinitionStorage definitionStorage) : IMessageSubscriptionStorage
{
    public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() =>
        QueryAsync<MessageSubscription>("SELECT body FROM {schema}.message_subscriptions");

    public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) =>
        QueryAsync<MessageSubscription>(
            "SELECT body FROM {schema}.message_subscriptions WHERE message_name = @name AND correlation_key IS NOT DISTINCT FROM @correlationKey AND process_instance_id IS NOT DISTINCT FROM @instanceId",
            ("name", messageName), ("correlationKey", (object?)correlationKey ?? DBNull.Value), ("instanceId", (object?)messageInstanceId ?? DBNull.Value));

    public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) =>
        QueryAsync<MessageSubscription>("SELECT body FROM {schema}.message_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public Task AddMessageSubscription(MessageSubscription messageSubscription) => ExecuteAsync(
        "INSERT INTO {schema}.message_subscriptions (id, related_definition_id, process_instance_id, message_name, correlation_key, body) VALUES (@id, @relatedDefinitionId, @instanceId, @name, @correlationKey, @body)",
        ("id", Guid.NewGuid()),
        ("relatedDefinitionId", messageSubscription.RelatedDefinitionId),
        ("instanceId", (object?)messageSubscription.ProcessInstanceId ?? DBNull.Value),
        ("name", messageSubscription.Message.Name),
        ("correlationKey", (object?)messageSubscription.Message.FlowzerCorrelationKey ?? DBNull.Value),
        ("body", StorageJson.Serialize(messageSubscription)));

    public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) =>
        ExecuteAsync("DELETE FROM {schema}.message_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) =>
        ExecuteAsync("DELETE FROM {schema}.message_subscriptions WHERE related_definition_id = @relatedDefinitionId AND process_instance_id IS NULL", ("relatedDefinitionId", metaDefinitionId));

    public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) =>
        ExecuteAsync("DELETE FROM {schema}.signal_subscriptions WHERE related_definition_id = @relatedDefinitionId AND process_instance_id IS NULL", ("relatedDefinitionId", relatedDefinitionId));

    public void AddSignalSubscription(SignalSubscription signalSubscription) => Execute(
        "INSERT INTO {schema}.signal_subscriptions (id, related_definition_id, process_instance_id, signal_name, body) VALUES (@id, @relatedDefinitionId, @instanceId, @name, @body)",
        ("id", Guid.NewGuid()),
        ("relatedDefinitionId", signalSubscription.RelatedDefinitionId),
        ("instanceId", (object?)signalSubscription.ProcessInstanceId ?? DBNull.Value),
        ("name", signalSubscription.Signal),
        ("body", StorageJson.Serialize(signalSubscription)));

    public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) =>
        QueryAsync<SignalSubscription>("SELECT body FROM {schema}.signal_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) =>
        Execute("DELETE FROM {schema}.signal_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
        QueryAsync<UserTaskSubscription>("SELECT body FROM {schema}.user_task_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public async Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
    {
        var subscriptions = (await QueryAsync<ExtendedUserTaskSubscription>("SELECT body FROM {schema}.user_task_subscriptions")).ToList();
        var metaNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var versions = new Dictionary<Guid, Model.Version>();

        foreach (var subscription in subscriptions)
        {
            if (!metaNames.TryGetValue(subscription.MetaDefinitionId, out var metaName))
            {
                metaName = await ResolveMetaName(subscription.MetaDefinitionId);
                metaNames[subscription.MetaDefinitionId] = metaName;
            }

            if (!versions.TryGetValue(subscription.DefinitionId, out var version))
            {
                version = await ResolveVersion(subscription.DefinitionId);
                versions[subscription.DefinitionId] = version;
            }

            subscription.DefinitionMetaName = metaName;
            subscription.DefinitionVersion = version;
        }

        return subscriptions;
    }

    public async Task<ExtendedUserTaskSubscription?> GetUserTaskExtended(Guid userTaskId)
    {
        var subscriptions = (await QueryAsync<ExtendedUserTaskSubscription>(
            "SELECT body FROM {schema}.user_task_subscriptions WHERE id = @id",
            [("id", userTaskId)])).ToList();

        var subscription = subscriptions.SingleOrDefault();
        if (subscription is null)
        {
            return null;
        }

        subscription.DefinitionMetaName = await ResolveMetaName(subscription.MetaDefinitionId);
        subscription.DefinitionVersion = await ResolveVersion(subscription.DefinitionId);
        return subscription;
    }

    public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => ExecuteAsync("""
        INSERT INTO {schema}.user_task_subscriptions (id, related_definition_id, process_instance_id, body)
        VALUES (@id, @relatedDefinitionId, @instanceId, @body)
        ON CONFLICT (id) DO UPDATE SET related_definition_id = EXCLUDED.related_definition_id, process_instance_id = EXCLUDED.process_instance_id, body = EXCLUDED.body
        """,
        ("id", userTasks.Id),
        ("relatedDefinitionId", userTasks.MetaDefinitionId),
        ("instanceId", (object?)userTasks.ProcessInstanceId ?? DBNull.Value),
        ("body", StorageJson.Serialize(userTasks)));

    public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) =>
        ExecuteAsync("DELETE FROM {schema}.user_task_subscriptions WHERE id = @id", ("id", userTaskSubscriptionId));

    public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) =>
        Execute("DELETE FROM {schema}.user_task_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) =>
        ExecuteAsync("DELETE FROM {schema}.user_task_subscriptions WHERE related_definition_id = @relatedDefinitionId AND process_instance_id IS NULL", ("relatedDefinitionId", relatedDefinitionId));

    public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() =>
        QueryAsync<TimerSubscription>("SELECT body FROM {schema}.timer_subscriptions ORDER BY due_at");

    public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) =>
        QueryAsync<TimerSubscription>("SELECT body FROM {schema}.timer_subscriptions WHERE process_instance_id = @instanceId ORDER BY due_at", ("instanceId", instanceId));

    public Task AddTimerSubscription(TimerSubscription timerSubscription) => ExecuteAsync("""
        INSERT INTO {schema}.timer_subscriptions (id, related_definition_id, process_instance_id, due_at, body)
        VALUES (@id, @relatedDefinitionId, @instanceId, @dueAt, @body)
        ON CONFLICT (id) DO UPDATE SET related_definition_id = EXCLUDED.related_definition_id, process_instance_id = EXCLUDED.process_instance_id, due_at = EXCLUDED.due_at, body = EXCLUDED.body
        """,
        ("id", timerSubscription.Id),
        ("relatedDefinitionId", timerSubscription.RelatedDefinitionId),
        ("instanceId", (object?)timerSubscription.ProcessInstanceId ?? DBNull.Value),
        ("dueAt", DateTime.SpecifyKind(timerSubscription.DueAt, DateTimeKind.Utc)),
        ("body", StorageJson.Serialize(timerSubscription)));

    public Task RemoveTimerSubscription(Guid timerSubscriptionId) =>
        ExecuteAsync("DELETE FROM {schema}.timer_subscriptions WHERE id = @id", ("id", timerSubscriptionId));

    public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) =>
        ExecuteAsync("DELETE FROM {schema}.timer_subscriptions WHERE process_instance_id = @instanceId", ("instanceId", instanceId));

    public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) =>
        ExecuteAsync("DELETE FROM {schema}.timer_subscriptions WHERE related_definition_id = @relatedDefinitionId AND process_instance_id IS NULL", ("relatedDefinitionId", relatedDefinitionId));

    private async Task<string> ResolveMetaName(string metaDefinitionId)
    {
        try
        {
            return (await definitionStorage.GetMetaDefinitionById(metaDefinitionId)).Name;
        }
        catch (DefinitionStorageNotFoundException)
        {
            return metaDefinitionId;
        }
    }

    private async Task<Model.Version> ResolveVersion(Guid definitionId)
    {
        try
        {
            return (await definitionStorage.GetDefinitionById(definitionId)).Version;
        }
        catch (DefinitionStorageNotFoundException)
        {
            return new Model.Version(0, 0);
        }
    }

    private Task<IEnumerable<T>> QueryAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        session.RunAsync<IEnumerable<T>>(async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, sql, parameters);
            return await PostgreSqlDefinitionStorage.ReadBodiesAsync<T>(command);
        });

    private Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, sql, parameters);
            await command.ExecuteNonQueryAsync();
        });

    private void Execute(string sql, params (string Name, object Value)[] parameters) =>
        session.Run((connection, transaction) =>
        {
            using var command = CreateCommand(connection, transaction, sql, parameters);
            return command.ExecuteNonQuery();
        });

    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, (string Name, object Value)[] parameters)
    {
        var command = session.CreateCommand(connection, transaction, sql);
        foreach (var (name, value) in parameters)
        {
            // Nullable Spalten typisiert uebergeben, damit Npgsql den Typ bei DBNull nicht raten muss.
            var parameter = command.Parameters.AddWithValue(name, value);
            if (value is DBNull)
            {
                parameter.NpgsqlDbType = name.EndsWith("Id", StringComparison.Ordinal) ? NpgsqlTypes.NpgsqlDbType.Uuid : NpgsqlTypes.NpgsqlDbType.Text;
            }
        }

        return command;
    }
}
