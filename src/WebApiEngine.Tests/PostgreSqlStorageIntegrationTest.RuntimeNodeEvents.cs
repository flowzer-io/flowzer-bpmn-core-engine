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

    // Testzweck: Die Auswertungsabfrage liest je gebundener Version und Zeitraum — der Anfang
    // zaehlt mit, das Ende nicht —, sie beantwortet mehrere Versionen in einem Lauf und laesst
    // fremde Versionen draussen. Die Reihenfolge bleibt die stabile Zeitreihenfolge.
    [Test]
    public async Task RuntimeNodeEvents_ShouldBeQueryableByDefinitionAndPeriod()
    {
        var wanted = Guid.NewGuid();
        var alsoWanted = Guid.NewGuid();
        var origin = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        var atStart = CreateRuntimeNodeEvent(wanted, origin.AddHours(-1));
        var inside = CreateRuntimeNodeEvent(alsoWanted, origin);
        var atEnd = CreateRuntimeNodeEvent(wanted, origin.AddHours(1));
        var tooEarly = CreateRuntimeNodeEvent(wanted, origin.AddHours(-2));
        var otherVersion = CreateRuntimeNodeEvent(Guid.NewGuid(), origin);
        using var writer = new PostgreSqlStorage(_dataSource!, Schema);
        foreach (var item in new[] { atStart, inside, atEnd, tooEarly, otherVersion })
        {
            (await writer.RuntimeNodeEventStorage.AppendIfAbsent(item)).Should().BeTrue();
        }

        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var stored = await reader.RuntimeNodeEventStorage.GetByDefinitionIds(
            [wanted, alsoWanted], origin.AddHours(-1), origin.AddHours(1));

        stored.Select(item => item.Id).Should().Equal(atStart.Id, inside.Id);
    }

    // Testzweck: Ohne Versionsauswahl darf die Ablage nicht den ganzen Bestand liefern — und
    // schon gar keine Abfrage ohne Einschraenkung absetzen.
    [Test]
    public async Task RuntimeNodeEvents_ShouldReturnNothingForAnEmptyDefinitionSelection()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        await storage.RuntimeNodeEventStorage.AppendIfAbsent(CreateRuntimeNodeEvent());

        (await storage.RuntimeNodeEventStorage.GetByDefinitionIds(
            [], DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddYears(1))).Should().BeEmpty();
    }

    private static RuntimeNodeEvent CreateRuntimeNodeEvent(
        Guid? definitionId = null,
        DateTimeOffset? occurredAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = Guid.NewGuid(),
        DefinitionId = definitionId ?? Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ReviewTask",
        State = FlowNodeState.Completed,
        CorrelationId = Guid.NewGuid(),
        OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow
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
