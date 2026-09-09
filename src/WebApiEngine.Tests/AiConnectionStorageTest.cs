using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Dateisystemvertrag fuer KI-Verbindungsmetadaten im Einzelprozessbetrieb.</summary>
[NonParallelizable]
public sealed class AiConnectionStorageTest
{
    // Testzweck: Create und Update sind revisionsgeschuetzt und ein veralteter Schreiber kann
    // weder Metadaten noch Secret-Referenz der aktuellen Fassung ueberschreiben.
    [Test]
    public async Task FilesystemStorage_ShouldCompareAndSwapAiConnections()
    {
        using var context = new Context();
        var initial = Connection(context.Id, "OpenAI", revision: 1, "env:FLOWZER_AI_FIRST");

        (await context.Storage.AiConnectionStorage.TryCreate(initial)).Status.Should().Be(AiConnectionWriteStatus.Written);
        var updated = initial with { Name = "OpenAI EU", Revision = 2, SecretReference = "env:FLOWZER_AI_SECOND" };
        (await context.Storage.AiConnectionStorage.TryUpdate(updated, 1)).Status.Should().Be(AiConnectionWriteStatus.Written);
        var stale = await context.Storage.AiConnectionStorage.TryUpdate(
            updated with { Name = "Veraltet", Revision = 2, SecretReference = "env:FLOWZER_AI_STALE" }, 1);

        stale.Status.Should().Be(AiConnectionWriteStatus.Conflict);
        stale.CurrentRevision.Should().Be(2);
        (await context.Storage.AiConnectionStorage.Get(context.Id))!.Should().Be(updated);
    }

    // Testzweck: Namen sind installationsweit ohne Beachtung der Gross-/Kleinschreibung
    // eindeutig, damit Modellierende eine Verbindung nicht mit einem Namenszwilling verwechseln.
    [Test]
    public async Task FilesystemStorage_ShouldRejectCaseInsensitiveDuplicateNames()
    {
        using var context = new Context();
        await context.Storage.AiConnectionStorage.TryCreate(Connection(context.Id, "Produktion", 1, "env:FLOWZER_AI_ONE"));

        var duplicate = await context.Storage.AiConnectionStorage.TryCreate(
            Connection(Guid.NewGuid(), "produktion", 1, "env:FLOWZER_AI_TWO"));

        duplicate.Status.Should().Be(AiConnectionWriteStatus.Conflict);
        (await context.Storage.AiConnectionStorage.List()).Should().ContainSingle();
    }

    private static AiConnection Connection(Guid id, string name, long revision, string secretReference) => new(
        id,
        name,
        AiProviderKind.OpenAi,
        AiProcessingLocation.Cloud,
        null,
        "gpt-example",
        secretReference,
        true,
        revision,
        new DateTimeOffset(2026, 9, 9, 16, 0, 0, TimeSpan.Zero),
        Guid.Parse("C2222222-2222-4222-8222-222222222222"));

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-ai-connections-{Guid.NewGuid():N}");

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }

        public Storage Storage { get; }
        public Guid Id { get; } = Guid.NewGuid();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
