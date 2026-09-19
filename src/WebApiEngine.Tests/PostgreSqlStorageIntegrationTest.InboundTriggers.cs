using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Ein Ausloeser laesst sich anlegen, ueber seine oeffentliche Adresse finden und
    // wieder entfernen — auch abgeschaltet, denn erst der Aufrufer entscheidet, ob daraus eine
    // Antwort oder eine Ablehnung wird.
    [Test]
    public async Task InboundTriggerStorage_ShouldStoreAndResolveTriggersByKey()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var id = Guid.NewGuid();
        var trigger = InboundTriggerStorageTest.Trigger(id, "pg-abc123");

        await storage.InboundTriggerStorage.Save(trigger);
        trigger.Enabled = false;
        await storage.InboundTriggerStorage.Save(trigger);

        (await storage.InboundTriggerStorage.GetByKey("pg-abc123"))!.Id.Should().Be(id);
        (await storage.InboundTriggerStorage.GetByKey("pg-abc123"))!.Enabled.Should().BeFalse();
        (await storage.InboundTriggerStorage.GetByKey("gibtesnicht")).Should().BeNull();
        (await storage.InboundTriggerStorage.GetAll()).Should().ContainSingle();
        (await storage.InboundTriggerStorage.Remove(id)).Should().BeTrue();
        (await storage.InboundTriggerStorage.Remove(id)).Should().BeFalse();
    }

    // Testzweck: Die Adresse ist in der Datenbank eindeutig. Zwei Ausloeser unter demselben
    // Schluessel liessen nicht mehr entscheiden, welcher Workflow gemeint ist.
    [Test]
    public async Task InboundTriggerStorage_ShouldRejectADuplicateKey()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        await storage.InboundTriggerStorage.Save(InboundTriggerStorageTest.Trigger(Guid.NewGuid(), "pg-dup"));

        var duplicate = () => storage.InboundTriggerStorage.Save(
            InboundTriggerStorageTest.Trigger(Guid.NewGuid(), "pg-dup"));

        await duplicate.Should().ThrowAsync<Npgsql.PostgresException>();
        (await storage.InboundTriggerStorage.GetAll()).Should().ContainSingle();
    }

    // Testzweck: Zwei Prozesse zaehlen denselben Ausloeser ohne Verlust hoch. Wuerde der Zaehler
    // im Anwendungsspeicher gerechnet, zaehlten gleichzeitige Aufrufe als einer.
    [Test]
    public async Task InboundTriggerStorage_ShouldCountUsesAtomicallyAcrossProcesses()
    {
        var first = new PostgreSqlStorage(_dataSource!, Schema);
        var second = new PostgreSqlStorage(_dataSource!, Schema);
        var id = Guid.NewGuid();
        await first.InboundTriggerStorage.Save(InboundTriggerStorageTest.Trigger(id, "pg-count"));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(attempt => attempt % 2 == 0
            ? first.InboundTriggerStorage.RecordUse(id, Moment)
            : second.InboundTriggerStorage.RecordUse(id, Moment)));

        (await first.InboundTriggerStorage.Get(id))!.UseCount.Should().Be(10);
    }

    // Testzweck: Nutzungsstand und Fehlerfelder liegen ausserhalb des JSON-Koerpers. Eine
    // Umbenennung mit einem veralteten Objekt darf den Zaehler nicht zuruecksetzen.
    [Test]
    public async Task InboundTriggerStorage_ShouldKeepCountersOutOfManagementWrites()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var id = Guid.NewGuid();
        var trigger = InboundTriggerStorageTest.Trigger(id, "pg-keep");
        await storage.InboundTriggerStorage.Save(trigger);
        await storage.InboundTriggerStorage.RecordUse(id, Moment);
        await storage.InboundTriggerStorage.RecordFailure(id, Moment, "signature");

        trigger.Name = "Umbenannt";
        await storage.InboundTriggerStorage.Save(trigger);

        var stored = (await storage.InboundTriggerStorage.Get(id))!;
        stored.Name.Should().Be("Umbenannt");
        stored.UseCount.Should().Be(1);
        stored.LastUsedAt.Should().Be(Moment);
        stored.LastFailureReason.Should().Be("signature");
        stored.LastFailureAt.Should().Be(Moment);
    }

    private static readonly DateTime Moment = new(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);
}
