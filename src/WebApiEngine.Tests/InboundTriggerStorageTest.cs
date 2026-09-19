using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

/// <summary>Dateisystemvertrag für Auslöser im Einzelprozessbetrieb.</summary>
[NonParallelizable]
public sealed class InboundTriggerStorageTest
{
    // Testzweck: Ein Ausloeser laesst sich anlegen, ueber seine oeffentliche Adresse finden und
    // wieder entfernen. Ein abgeschalteter wird weiterhin gefunden: Erst der Aufrufer
    // entscheidet, ob daraus eine Antwort oder eine Ablehnung wird.
    [Test]
    public async Task FilesystemStorage_ShouldStoreAndResolveTriggersByKey()
    {
        using var context = new Context();
        var trigger = Trigger(context.Id, "abc123");

        await context.Storage.InboundTriggerStorage.Save(trigger);
        trigger.Enabled = false;
        await context.Storage.InboundTriggerStorage.Save(trigger);

        (await context.Storage.InboundTriggerStorage.GetByKey("abc123"))!.Id.Should().Be(context.Id);
        (await context.Storage.InboundTriggerStorage.GetByKey("abc123"))!.Enabled.Should().BeFalse();
        (await context.Storage.InboundTriggerStorage.GetByKey("gibtesnicht")).Should().BeNull();
        (await context.Storage.InboundTriggerStorage.GetAll()).Should().ContainSingle();
        (await context.Storage.InboundTriggerStorage.Remove(context.Id)).Should().BeTrue();
        (await context.Storage.InboundTriggerStorage.Remove(context.Id)).Should().BeFalse();
    }

    // Testzweck: Zwei Ausloeser duerfen nicht dieselbe Adresse tragen; sonst liesse sich nicht
    // mehr entscheiden, welcher Workflow gemeint ist.
    [Test]
    public async Task FilesystemStorage_ShouldRejectADuplicateKey()
    {
        using var context = new Context();
        await context.Storage.InboundTriggerStorage.Save(Trigger(context.Id, "abc123"));

        var duplicate = () => context.Storage.InboundTriggerStorage.Save(Trigger(Guid.NewGuid(), "abc123"));

        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        (await context.Storage.InboundTriggerStorage.GetAll()).Should().ContainSingle();
    }

    // Testzweck: Nutzungsstand und Fehlerfelder gehoeren nicht zum Verwaltungsschreiben. Eine
    // Umbenennung mit einem veralteten Objekt darf den Zaehler nicht zuruecksetzen.
    [Test]
    public async Task FilesystemStorage_ShouldKeepCountersOutOfManagementWrites()
    {
        using var context = new Context();
        var trigger = Trigger(context.Id, "abc123");
        await context.Storage.InboundTriggerStorage.Save(trigger);
        await context.Storage.InboundTriggerStorage.RecordUse(context.Id, Moment);
        await context.Storage.InboundTriggerStorage.RecordUse(context.Id, Moment);
        await context.Storage.InboundTriggerStorage.RecordFailure(context.Id, Moment, "signature");

        // Dasselbe Objekt wie vor den Aufrufen: Es kennt die inzwischen gezaehlten Aufrufe nicht.
        trigger.Name = "Umbenannt";
        await context.Storage.InboundTriggerStorage.Save(trigger);

        var stored = (await context.Storage.InboundTriggerStorage.Get(context.Id))!;
        stored.Name.Should().Be("Umbenannt");
        stored.UseCount.Should().Be(2);
        stored.LastUsedAt.Should().Be(Moment);
        stored.LastFailureReason.Should().Be("signature");
    }

    // Testzweck: Gleichzeitige Aufrufe desselben Ausloesers zaehlen einzeln. Ein
    // Lesen-Aendern-Schreiben ohne gemeinsame Sperre liesse sie als einen zaehlen.
    [Test]
    public async Task FilesystemStorage_ShouldCountConcurrentCallsIndividually()
    {
        using var context = new Context();
        await context.Storage.InboundTriggerStorage.Save(Trigger(context.Id, "abc123"));

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => context.Storage.InboundTriggerStorage.RecordUse(context.Id, Moment)));

        (await context.Storage.InboundTriggerStorage.Get(context.Id))!.UseCount.Should().Be(20);
    }

    // Testzweck: Der Zaehler eines nicht mehr vorhandenen Ausloesers legt nichts neu an; sonst
    // entstuende aus einem Aufruf gegen einen geloeschten Schluessel ein leerer Eintrag.
    [Test]
    public async Task FilesystemStorage_ShouldIgnoreCountersForAMissingTrigger()
    {
        using var context = new Context();

        await context.Storage.InboundTriggerStorage.RecordUse(context.Id, Moment);
        await context.Storage.InboundTriggerStorage.RecordFailure(context.Id, Moment, "signature");

        (await context.Storage.InboundTriggerStorage.GetAll()).Should().BeEmpty();
    }

    private static readonly DateTime Moment = new(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);

    internal static InboundTrigger Trigger(Guid id, string key) => new()
    {
        Id = id,
        Key = key,
        Name = "Ticketsystem",
        Kind = InboundTriggerKind.Start,
        DefinitionId = "order-process",
        VariablesMode = InboundTriggerVariablesMode.Fields,
        AllowedFields = ["orderId"],
        SecretHash = "aesgcm-v1$AAAA$BBBB$CCCC",
        Enabled = true,
        CreatedAt = new DateTime(2026, 9, 19, 7, 0, 0, DateTimeKind.Utc),
        CreatedBy = Guid.Parse("C4444444-4444-4444-8444-444444444444")
    };

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-inbound-triggers-{Guid.NewGuid():N}");

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
