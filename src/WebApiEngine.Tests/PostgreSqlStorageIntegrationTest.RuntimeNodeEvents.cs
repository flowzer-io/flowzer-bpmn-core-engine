using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Der PostgreSQL-Adapter dedupliziert parallele Wiederholungen über die stabile
    // Ereigniskennung und liefert die unveränderliche Spur instanzbezogen zurück.
    [Test]
    public async Task RuntimeNodeEvents_ShouldBeIdempotentUnderConcurrentAppends()
    {
        var item = CreateRuntimeNodeEvent();
        using var first = new PostgreSqlStorage(_dataSource!, Schema);
        using var second = new PostgreSqlStorage(_dataSource!, Schema);

        var results = await Task.WhenAll(
            first.RuntimeNodeEventStorage.AppendIfAbsent(item),
            second.RuntimeNodeEventStorage.AppendIfAbsent(item));

        results.Should().ContainSingle(result => result);
        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.RuntimeNodeEventStorage.GetByProcessInstance(item.ProcessInstanceId))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(item);
    }

    // Testzweck: Laufzeitereignis und Instanzzustand verwenden dieselbe PostgreSQL-Session;
    // ein Dispose ohne Commit verwirft daher beide Änderungen vollständig.
    [Test]
    public async Task RuntimeNodeEvents_ShouldRollbackTogetherWithTheSurroundingStorageSession()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var item = CreateRuntimeNodeEvent();
        var instance = CreateRuntimeEventInstance(item);

        using (var writer = provider.GetTransactionalStorage())
        {
            await writer.InstanceStorage.AddOrUpdateInstance(instance);
            (await writer.RuntimeNodeEventStorage.AppendIfAbsent(item)).Should().BeTrue();
        }

        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        await reader.InstanceStorage.Invoking(storage => storage.GetProcessInstance(item.ProcessInstanceId))
            .Should().ThrowAsync<FileNotFoundException>();
        (await reader.RuntimeNodeEventStorage.GetByProcessInstance(item.ProcessInstanceId)).Should().BeEmpty();
    }

    private static RuntimeNodeEvent CreateRuntimeNodeEvent() => new()
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = Guid.NewGuid(),
        DefinitionId = Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ReviewTask",
        State = FlowNodeState.Completed,
        CorrelationId = Guid.NewGuid(),
        OccurredAtUtc = DateTimeOffset.UtcNow
    };

    private static StorageSystem.ProcessInstanceInfo CreateRuntimeEventInstance(RuntimeNodeEvent item) => new()
    {
        InstanceId = item.ProcessInstanceId,
        metaDefinitionId = "runtime-event-test",
        DefinitionId = item.DefinitionId,
        ProcessId = "Process",
        Tokens = [],
        IsFinished = false,
        State = ProcessInstanceState.Waiting,
        MessageSubscriptionCount = 0,
        SignalSubscriptionCount = 0,
        UserTaskSubscriptionCount = 0,
        ServiceSubscriptionCount = 0
    };
}
