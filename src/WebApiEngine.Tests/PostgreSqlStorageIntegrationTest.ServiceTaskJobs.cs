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
