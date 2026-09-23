using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: PostgreSQL bindet den Heartbeat in einem Statement an Besitzer und eine noch
    // gueltige Lease; fremde und verspaetete Aufrufe koennen den Auftrag nicht wiederbeleben.
    [Test]
    public async Task ServiceTaskStorage_ShouldRenewOnlyTheCurrentUnexpiredLease()
    {
        var firstProcess = new PostgreSqlStorage(_dataSource!, Schema);
        var secondProcess = new PostgreSqlStorage(_dataSource!, Schema);
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var ownerA = ServiceTaskJobService.BuildLockOwner(Guid.NewGuid(), "worker-a");
        var ownerB = ServiceTaskJobService.BuildLockOwner(Guid.NewGuid(), "worker-a");
        var job = CreateServiceTaskJob(now);
        await firstProcess.ServiceTaskStorage.SaveJob(job);

        var claimed = (await firstProcess.ServiceTaskStorage.ClaimJobs(
            job.Type,
            ownerA,
            now,
            now.AddMinutes(5),
            1)).Single();
        var renewed = await secondProcess.ServiceTaskStorage.RenewJobLease(
            claimed.Id,
            ownerA,
            now.AddMinutes(1),
            now.AddMinutes(20));
        var foreign = await secondProcess.ServiceTaskStorage.RenewJobLease(
            claimed.Id,
            ownerB,
            now.AddMinutes(2),
            now.AddMinutes(30));
        var expired = await firstProcess.ServiceTaskStorage.RenewJobLease(
            claimed.Id,
            ownerA,
            now.AddMinutes(21),
            now.AddMinutes(40));

        renewed.Should().NotBeNull();
        renewed!.LockedUntil.Should().Be(now.AddMinutes(20));
        foreign.Should().BeNull();
        expired.Should().BeNull();
        (await secondProcess.ServiceTaskStorage.GetJob(claimed.Id))!.LockedUntil
            .Should().Be(now.AddMinutes(20));
    }

    // Testzweck: Das Umbinden auf die Zielversion laeuft in der Transaktion der Migration, die
    // Vergabe eines Auftrags aber ohne sie. Eine Lease, die nach dem Lesen und vor dem Umbinden
    // erteilt oder verlaengert wurde, muss erhalten bleiben — in Spalten wie im Rumpf.
    [Test]
    public async Task ServiceTaskStorage_ShouldRebindWithoutOverwritingALeaseFromAnotherSession()
    {
        var worker = new PostgreSqlStorage(_dataSource!, Schema);
        var now = new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc);
        var owner = ServiceTaskJobService.BuildLockOwner(Guid.NewGuid(), "worker-migration");
        var job = CreateServiceTaskJob(now);
        await worker.ServiceTaskStorage.SaveJob(job);
        var target = Guid.NewGuid();

        using var migration = new PostgreSqlTransactionalStorage(_dataSource!, Schema);
        // Wie die Vorschau: ungesperrt lesen, bevor die zweite Sitzung den Auftrag uebernimmt.
        (await migration.ServiceTaskStorage.GetJobs()).Should().ContainSingle()
            .Which.LockedBy.Should().BeNull();

        var claimed = (await worker.ServiceTaskStorage.ClaimJobs(job.Type, owner, now, now.AddMinutes(5), 1))
            .Should().ContainSingle().Subject;
        (await worker.ServiceTaskStorage.RenewJobLease(claimed.Id, owner, now.AddMinutes(1), now.AddMinutes(20)))
            .Should().NotBeNull();

        (await migration.ServiceTaskStorage.RebindJobsOfInstance(job.ProcessInstanceId, target)).Should().Be(1);
        migration.CommitChanges();

        var stored = (await worker.ServiceTaskStorage.GetJob(job.Id))!;
        stored.DefinitionId.Should().Be(target);
        stored.LockedBy.Should().Be(owner);
        stored.LockedUntil.Should().Be(now.AddMinutes(20));
        stored.Retries.Should().Be(job.Retries);
        // Der Rumpf traegt dieselbe Vergabe wie die Spalten; ein Leser ohne Spaltenabgleich
        // duerfte sonst eine laengst ueberholte Sperre sehen.
        var body = await ReadJobBodyAsync(job.Id);
        body.Should().Contain(owner);
        body.Should().Contain(target.ToString());
    }

    // Testzweck: Das Umbinden gehoert genau einer Instanz; die Auftraege anderer Instanzen
    // duerfen dabei nicht auf deren Zielversion wandern.
    [Test]
    public async Task ServiceTaskStorage_ShouldRebindOnlyTheJobsOfOneInstance()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var own = CreateServiceTaskJob(now);
        var foreign = CreateServiceTaskJob(now);
        await storage.ServiceTaskStorage.SaveJob(own);
        await storage.ServiceTaskStorage.SaveJob(foreign);
        var target = Guid.NewGuid();

        (await storage.ServiceTaskStorage.RebindJobsOfInstance(own.ProcessInstanceId, target)).Should().Be(1);

        (await storage.ServiceTaskStorage.GetJob(own.Id))!.DefinitionId.Should().Be(target);
        (await storage.ServiceTaskStorage.GetJob(foreign.Id))!.DefinitionId.Should().Be(foreign.DefinitionId);
    }

    // Testzweck: Die Spur der Freigaben von Hand liegt im Rumpf und muss ueber Speichern, Lesen
    // und Vergeben erhalten bleiben. Ginge sie beim naechsten Anspruch verloren, waere die
    // Auditspur genau dann leer, wenn der freigegebene Auftrag tatsaechlich wieder lief.
    [Test]
    public async Task ServiceTaskStorage_ShouldKeepTheManualRetryTrailOnPostgreSql()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        var actor = Guid.NewGuid();
        var job = CreateServiceTaskJob(now);
        job.Retries = 0;
        job.RetryHistory.Add(new ServiceTaskJobRetry
        {
            At = now,
            By = actor,
            Retries = 2,
            CorrectedKeys = ["iban"]
        });
        await storage.ServiceTaskStorage.SaveJob(job);

        job.Retries = 2;
        await storage.ServiceTaskStorage.SaveJob(job);
        var owner = ServiceTaskJobService.BuildLockOwner(actor, "worker-a");
        var claimed = (await storage.ServiceTaskStorage.ClaimJobs(job.Type, owner, now, now.AddMinutes(5), 1))
            .Should().ContainSingle().Subject;

        var entry = claimed.RetryHistory.Should().ContainSingle().Subject;
        entry.By.Should().Be(actor);
        entry.At.Should().Be(now);
        entry.Retries.Should().Be(2);
        entry.CorrectedKeys.Should().Equal("iban");
        // Nur die Namen, nie die Werte: Die Spur wird gelesen, wenn niemand mehr weiss, was in
        // den Feldern stand.
        (await ReadJobBodyAsync(job.Id)).Should().NotContain("DE02120300000000202051");
    }

    private async Task<string> ReadJobBodyAsync(Guid jobId)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT body FROM {Schema}.service_task_jobs WHERE id = @id";
        command.Parameters.AddWithValue("id", jobId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static ServiceTaskJob CreateServiceTaskJob(DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        Type = "flowzer.ai",
        Name = "Lang laufender Auftrag",
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ServiceTask_AI",
        ProcessInstanceId = Guid.NewGuid(),
        MetaDefinitionId = "ai-demo",
        DefinitionId = Guid.NewGuid(),
        ProcessId = "Process_AI",
        CreatedAt = createdAt,
        Retries = 3
    };
}
