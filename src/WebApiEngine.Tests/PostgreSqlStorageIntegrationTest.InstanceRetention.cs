using FluentAssertions;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    private static readonly DateTime RetentionFinishedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // Testzweck: PostgreSQL loescht dieselbe Datenartenliste wie die Dateiablage. Die Zusicherungen
    // stammen aus derselben Fixture; eine Ablage, die eine Datenart liegen laesst, faellt damit auf,
    // statt sich hinter einem eigenen, schwaecheren Test zu verstecken.
    [Test]
    public async Task PurgeInstance_ShouldMirrorFilesystemContract()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var seeded = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt);
        await InstancePurgeFixture.AssertIntactAsync(storage, seeded);

        await PurgeInTransactionAsync(seeded.InstanceId);

        await InstancePurgeFixture.AssertPurgedAsync(storage, seeded);
    }

    // Testzweck: Auch in PostgreSQL bleibt eine zweite Instanz vollstaendig unberuehrt. Ein
    // DELETE ohne Instanzbedingung faellt sonst erst im Produktivbestand auf.
    [Test]
    public async Task PurgeInstance_ShouldLeaveOtherInstancesUntouchedInPostgreSql()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var victim = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt);
        var bystander = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt);

        await PurgeInTransactionAsync(victim.InstanceId);

        await InstancePurgeFixture.AssertPurgedAsync(storage, victim);
        await InstancePurgeFixture.AssertIntactAsync(storage, bystander);
    }

    // Testzweck: Die Auswahlmenge der Aufbewahrung nutzt in PostgreSQL den Index auf is_finished
    // und darf dabei keine laufende Instanz liefern.
    [Test]
    public async Task GetAllFinishedInstances_ShouldNeverReturnARunningInstanceInPostgreSql()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var running = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt, finished: false);
        var finished = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt);

        var candidates = (await storage.InstanceStorage.GetAllFinishedInstances()).ToArray();

        candidates.Select(instance => instance.InstanceId).Should().BeEquivalentTo([finished.InstanceId]);
        candidates.Should().NotContain(instance => instance.InstanceId == running.InstanceId);
    }

    // Testzweck: Ein Abbruch der Transaktion laesst die Instanz samt allem Angehaengten stehen.
    // Das ist die Zusage, die PostgreSQL gegenueber der Dateiablage zusaetzlich gibt: ganz oder
    // gar nicht, kein halb geloeschter Vorgang.
    [Test]
    public async Task PurgeInstance_ShouldLeaveNothingDeleted_WhenTheTransactionIsRolledBack()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var seeded = await InstancePurgeFixture.SeedAsync(storage, RetentionFinishedAt);

        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        using (var attempt = provider.GetTransactionalStorage())
        {
            await InstancePurge.ExecuteAsync(attempt, seeded.InstanceId);
            attempt.RollbackTransaction();
        }

        await InstancePurgeFixture.AssertIntactAsync(storage, seeded);
    }

    /// <summary>Loescht wie der Produktivpfad: alles in genau einer Transaktion.</summary>
    private async Task PurgeInTransactionAsync(Guid instanceId)
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        using var transactionalStorage = provider.GetTransactionalStorage();
        await InstancePurge.ExecuteAsync(transactionalStorage, instanceId);
        transactionalStorage.CommitChanges();
    }
}
