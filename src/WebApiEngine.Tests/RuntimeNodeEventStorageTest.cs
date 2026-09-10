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

    private static RuntimeNodeEvent Create(Guid? processInstanceId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = processInstanceId ?? Guid.NewGuid(),
        DefinitionId = Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ReviewTask",
        State = FlowNodeState.Active,
        CorrelationId = Guid.NewGuid(),
        OccurredAtUtc = DateTimeOffset.UtcNow
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
