using Model;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Transaktional an die umgebende Storage-Session gebundene append-only Runtime-Ereignisspur.
/// Der Primärschlüssel ist die stabile Ereigniskennung und dedupliziert Wiederholungen atomar.
/// </summary>
internal sealed class PostgreSqlRuntimeNodeEventStorage(PostgreSqlSession session) : IRuntimeNodeEventStorage
{
    public Task<bool> AppendIfAbsent(RuntimeNodeEvent runtimeEvent) => session.RunAsync(async (connection, transaction) =>
    {
        RuntimeNodeEventContract.Validate(runtimeEvent);
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.runtime_node_events
                (id, process_instance_id, definition_id, token_id, flow_node_id, node_state,
                 correlation_id, occurred_at, body)
            VALUES (@id, @processInstanceId, @definitionId, @tokenId, @flowNodeId, @state,
                    @correlationId, @occurredAt, @body)
            ON CONFLICT (id) DO NOTHING
            RETURNING id
            """);
        command.Parameters.AddWithValue("id", runtimeEvent.Id);
        command.Parameters.AddWithValue("processInstanceId", runtimeEvent.ProcessInstanceId);
        command.Parameters.AddWithValue("definitionId", runtimeEvent.DefinitionId);
        command.Parameters.AddWithValue("tokenId", runtimeEvent.TokenId);
        command.Parameters.AddWithValue("flowNodeId", runtimeEvent.FlowNodeId);
        command.Parameters.AddWithValue("state", (short)runtimeEvent.State);
        command.Parameters.AddWithValue("correlationId", runtimeEvent.CorrelationId);
        command.Parameters.AddWithValue("occurredAt", runtimeEvent.OccurredAtUtc);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(runtimeEvent));
        return await command.ExecuteScalarAsync() is not null;
    });

    public Task<IReadOnlyList<RuntimeNodeEvent>> GetByProcessInstance(Guid processInstanceId)
    {
        if (processInstanceId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(processInstanceId));
        return session.RunAsync<IReadOnlyList<RuntimeNodeEvent>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.runtime_node_events
                WHERE process_instance_id = @processInstanceId
                ORDER BY occurred_at, token_id, id
                """);
            command.Parameters.AddWithValue("processInstanceId", processInstanceId);
            return await ReadAllAsync(command);
        });
    }

    public Task<IReadOnlyList<RuntimeNodeEvent>> GetByDefinitionIds(
        IReadOnlyCollection<Guid> definitionIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        if (toUtc < fromUtc) throw new ArgumentOutOfRangeException(nameof(toUtc));
        if (definitionIds.Count == 0) return Task.FromResult<IReadOnlyList<RuntimeNodeEvent>>([]);

        return session.RunAsync<IReadOnlyList<RuntimeNodeEvent>>(async (connection, transaction) =>
        {
            // definition_id = ANY(...) statt IN mit erzeugter Parameterliste: Der Plan bleibt
            // derselbe, egal wie viele Versionen der Katalogeintrag hat, und die Abfrage trifft
            // den Index (definition_id, occurred_at) aus Migration 018.
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.runtime_node_events
                WHERE definition_id = ANY(@definitionIds)
                  AND occurred_at >= @fromUtc AND occurred_at < @toUtc
                ORDER BY occurred_at, token_id, id
                """);
            command.Parameters.AddWithValue("definitionIds", definitionIds.Distinct().ToArray());
            command.Parameters.AddWithValue("fromUtc", fromUtc);
            command.Parameters.AddWithValue("toUtc", toUtc);
            return await ReadAllAsync(command);
        });
    }

    private static async Task<IReadOnlyList<RuntimeNodeEvent>> ReadAllAsync(Npgsql.NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<RuntimeNodeEvent>();
        while (await reader.ReadAsync()) result.Add(StorageJson.Deserialize<RuntimeNodeEvent>(reader.GetString(0)));
        return result;
    }
}
