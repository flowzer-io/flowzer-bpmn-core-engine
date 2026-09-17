using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

/// <summary>
/// Das Umbinden der Auftraege einer Instanz auf die Zielversion in der Dateiablage. Die
/// Vergabe laeuft dort unter einer eigenen Prozesssperre und ohne die Engine-Sperre; das
/// Umbinden muss sich in genau diese Sperre einreihen.
/// </summary>
[NonParallelizable]
public sealed class ServiceTaskJobRebindTest
{
    // Testzweck: Zwischen dem Lesen eines Auftrags und seinem Umbinden kann ein Worker ihn
    // uebernehmen. Bleibt die Lease dabei nicht erhalten, holt sich ein zweiter Worker denselben
    // Auftrag — ein Service-Task mit Seiteneffekt liefe doppelt.
    [Test]
    public async Task RebindJobsOfInstance_ShouldKeepALeaseTakenAfterTheJobWasRead()
    {
        using var context = new StorageContext();
        var storage = context.Storage.ServiceTaskStorage;
        var instanceId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var job = CreateJob(instanceId);
        await storage.SaveJob(job);

        // Der Stand, den ein Aufrufer vor der Vergabe gelesen haette.
        var stale = (await storage.GetJob(job.Id))!;
        stale.LockedBy.Should().BeNull();
        var claimed = (await storage.ClaimJobs("fetch", "worker-1", now, now.AddMinutes(5), 5))
            .Should().ContainSingle().Subject;
        (await storage.RenewJobLease(job.Id, "worker-1", now.AddMinutes(1), now.AddMinutes(20)))
            .Should().NotBeNull();

        (await storage.RebindJobsOfInstance(instanceId, target)).Should().Be(1);

        var rebound = (await storage.GetJob(job.Id))!;
        rebound.DefinitionId.Should().Be(target);
        rebound.LockedBy.Should().Be(claimed.LockedBy);
        rebound.LockedUntil.Should().Be(now.AddMinutes(20));
        rebound.Retries.Should().Be(job.Retries);
    }

    // Testzweck: Das Umbinden gehoert der Instanz und nicht der Ablage insgesamt; Auftraege
    // anderer Instanzen duerfen dabei nicht auf die fremde Zielversion wandern.
    [Test]
    public async Task RebindJobsOfInstance_ShouldTouchOnlyTheJobsOfThatInstance()
    {
        using var context = new StorageContext();
        var storage = context.Storage.ServiceTaskStorage;
        var instanceId = Guid.NewGuid();
        var own = CreateJob(instanceId);
        var foreign = CreateJob(Guid.NewGuid());
        await storage.SaveJob(own);
        await storage.SaveJob(foreign);
        var target = Guid.NewGuid();

        (await storage.RebindJobsOfInstance(instanceId, target)).Should().Be(1);

        (await storage.GetJob(own.Id))!.DefinitionId.Should().Be(target);
        (await storage.GetJob(foreign.Id))!.DefinitionId.Should().Be(foreign.DefinitionId);
    }

    private static ServiceTaskJob CreateJob(Guid processInstanceId) => new()
    {
        Id = Guid.NewGuid(),
        Type = "fetch",
        Name = "Daten holen",
        TokenId = Guid.NewGuid(),
        FlowNodeId = "Fetch",
        ProcessInstanceId = processInstanceId,
        MetaDefinitionId = "migration-catalog",
        DefinitionId = Guid.NewGuid(),
        ProcessId = "Process_Migration",
        CreatedAt = DateTime.UtcNow,
        Retries = 3
    };

    private sealed class StorageContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public StorageContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(
                Path.GetTempPath(), "flowzer-service-task-rebind-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);
            Storage = new Storage();
        }

        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
