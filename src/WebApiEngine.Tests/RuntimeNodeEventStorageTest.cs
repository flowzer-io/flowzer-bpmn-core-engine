using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

/// <summary>Belegt die append-only Laufzeitereignisspur des Datei-Entwicklungsadapters.</summary>
[NonParallelizable]
public sealed class RuntimeNodeEventStorageTest
{
    // Testzweck: Das erneute Persistieren desselben stabilen Ereignisses bleibt idempotent und
    // die ausgelesene Spur enthält ausschließlich die datensparsame Knotenzustandsänderung.
    [Test]
    public async Task AppendIfAbsent_ShouldBeIdempotentAndQueryableByProcessInstance()
    {
        using var context = new Context();
        var item = Create();

        (await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(item)).Should().BeTrue();
        (await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(item)).Should().BeFalse();

        var stored = await context.Storage.RuntimeNodeEventStorage.GetByProcessInstance(item.ProcessInstanceId);

        stored.Should().ContainSingle().Which.Should().BeEquivalentTo(item);
    }

    // Testzweck: Gleichzeitige Wiederholungen derselben Zustandsänderung erzeugen im
    // Einzelprozess-Entwicklungsadapter genau eine unveränderliche Ereignisdatei.
    [Test]
    public async Task ConcurrentAppends_ShouldPersistExactlyOneEvent()
    {
        using var context = new Context();
        var item = Create();

        var results = await Task.WhenAll(
            context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(item),
            context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(item));

        results.Should().ContainSingle(result => result);
        (await context.Storage.RuntimeNodeEventStorage.GetByProcessInstance(item.ProcessInstanceId))
            .Should().ContainSingle().Which.Id.Should().Be(item.Id);
    }

    // Testzweck: Die Auswertung liest je gebundener Version und Zeitraum. Der Anfang zaehlt mit,
    // das Ende nicht, fremde Versionen bleiben draussen, und die Reihenfolge bleibt dieselbe
    // stabile Zeitreihenfolge wie beim Lesen einer Instanz.
    [Test]
    public async Task GetByDefinitionIds_ShouldReturnOnlyTheChosenVersionsWithinThePeriod()
    {
        using var context = new Context();
        var wanted = Guid.NewGuid();
        var origin = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        var inside = Create(definitionId: wanted, occurredAtUtc: origin);
        var atStart = Create(definitionId: wanted, occurredAtUtc: origin.AddHours(-1));
        var atEnd = Create(definitionId: wanted, occurredAtUtc: origin.AddHours(1));
        var tooEarly = Create(definitionId: wanted, occurredAtUtc: origin.AddHours(-2));
        var otherVersion = Create(definitionId: Guid.NewGuid(), occurredAtUtc: origin);
        foreach (var item in new[] { inside, atStart, atEnd, tooEarly, otherVersion })
        {
            await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(item);
        }

        var stored = await context.Storage.RuntimeNodeEventStorage.GetByDefinitionIds(
            [wanted], origin.AddHours(-1), origin.AddHours(1));

        stored.Select(item => item.Id).Should().Equal(atStart.Id, inside.Id);
    }

    // Testzweck: Ohne Versionsauswahl gibt es nichts auszuwerten; die Ablage darf dann nicht
    // versehentlich den ganzen Bestand liefern.
    [Test]
    public async Task GetByDefinitionIds_ShouldReturnNothingForAnEmptySelection()
    {
        using var context = new Context();
        await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(Create());

        (await context.Storage.RuntimeNodeEventStorage.GetByDefinitionIds(
            [], DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Should().BeEmpty();
    }

    private static RuntimeNodeEvent Create(
        Guid? processInstanceId = null,
        Guid? definitionId = null,
        DateTimeOffset? occurredAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = processInstanceId ?? Guid.NewGuid(),
        DefinitionId = definitionId ?? Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ReviewTask",
        State = FlowNodeState.Active,
        CorrelationId = Guid.NewGuid(),
        OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow
    };

    private sealed class Context : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-runtime-events", Guid.NewGuid().ToString("N"));

        public Context()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }

        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previous);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
