using BPMN.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

/// <summary>
/// Vergabe und Rueckmeldung von Auftraegen an externe Worker. Die Engine fuehrt Service-Tasks
/// nicht selbst aus; sie macht sie als Auftrag sichtbar.
/// </summary>
[NonParallelizable]
public class ServiceTaskJobServiceTest
{
    // Testzweck: Ein freier Auftrag wird genau einem Worker zugeteilt und ist danach gesperrt.
    [Test]
    public async Task FetchAndLock_ShouldHandOutAJobOnlyOnce()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung");

        var first = await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5));
        var second = await context.Service.FetchAndLock("zahlung", "worker-b", 10, TimeSpan.FromMinutes(5));

        first.Should().ContainSingle();
        first.Single().LockedBy.Should().Be("worker-a");
        second.Should().BeEmpty();
    }

    // Testzweck: Aufträge anderer Typen bleiben unberuehrt; ein Worker bekommt nur seine Arbeit.
    [Test]
    public async Task FetchAndLock_ShouldOnlyReturnTheRequestedType()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung");
        await context.AddJob("versand");

        var jobs = await context.Service.FetchAndLock("versand", "worker-a", 10, TimeSpan.FromMinutes(5));

        jobs.Should().ContainSingle();
        jobs.Single().Type.Should().Be("versand");
    }

    // Testzweck: Laeuft die Frist ab, ohne dass der Worker zurueckmeldet, wird der Auftrag wieder
    // vergeben. Ein abgestuerzter Worker darf die Instanz nicht dauerhaft blockieren.
    [Test]
    public async Task FetchAndLock_ShouldReassignAJobAfterItsLockExpired()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung");
        await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5));

        context.Time.Advance(TimeSpan.FromMinutes(6));
        var afterExpiry = await context.Service.FetchAndLock("zahlung", "worker-b", 10, TimeSpan.FromMinutes(5));

        afterExpiry.Should().ContainSingle();
        afterExpiry.Single().LockedBy.Should().Be("worker-b");
    }

    // Testzweck: Nur der Worker, dem der Auftrag gehoert, darf zurueckmelden. Sonst koennten
    // zwei Ergebnisse fuer denselben Token den Prozess doppelt weiterfuehren.
    [Test]
    public async Task Complete_ShouldBeRefused_WhenTheJobBelongsToAnotherWorker()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung");
        var job = (await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        var result = await context.Service.Complete(job.Id, "worker-b", null, Guid.NewGuid());

        result.Should().Be(JobOperationResult.NotLockedByWorker);
    }

    // Testzweck: Eine abgelaufene Sperre wird als solche gemeldet, damit ein verspaeteter Worker
    // erkennt, dass inzwischen ein anderer uebernommen hat.
    [Test]
    public async Task Complete_ShouldReportAnExpiredLock()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung");
        var job = (await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        context.Time.Advance(TimeSpan.FromMinutes(6));
        var result = await context.Service.Complete(job.Id, "worker-a", null, Guid.NewGuid());

        result.Should().Be(JobOperationResult.LockExpired);
    }

    // Testzweck: Nach einem Fehlschlag wartet der Auftrag die vereinbarte Zeit und wird dann
    // erneut vergeben, mit einem Versuch weniger.
    [Test]
    public async Task Fail_ShouldReleaseTheJobForAnotherAttemptAfterTheBackoff()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung", retries: 3);
        var job = (await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        var result = await context.Service.Fail(job.Id, "worker-a", "Endpunkt nicht erreichbar", null, TimeSpan.FromSeconds(30));
        var immediately = await context.Service.FetchAndLock("zahlung", "worker-b", 10, TimeSpan.FromMinutes(5));

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var afterBackoff = await context.Service.FetchAndLock("zahlung", "worker-b", 10, TimeSpan.FromMinutes(5));

        result.Should().Be(JobOperationResult.Ok);
        immediately.Should().BeEmpty();
        afterBackoff.Should().ContainSingle();
        afterBackoff.Single().Retries.Should().Be(2);
        afterBackoff.Single().LastErrorMessage.Should().Be("Endpunkt nicht erreichbar");
    }

    // Testzweck: Ist der letzte Versuch verbraucht, wird der Auftrag nicht mehr vergeben. Er
    // bleibt sichtbar und wartet auf einen Eingriff, statt still zu verschwinden.
    [Test]
    public async Task Fail_ShouldStopHandingOutTheJob_WhenNoRetriesRemain()
    {
        var context = new JobTestContext();
        await context.AddJob("zahlung", retries: 1);
        var job = (await context.Service.FetchAndLock("zahlung", "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        await context.Service.Fail(job.Id, "worker-a", "endgueltig", null, TimeSpan.FromSeconds(1));
        context.Time.Advance(TimeSpan.FromMinutes(10));
        var afterwards = await context.Service.FetchAndLock("zahlung", "worker-b", 10, TimeSpan.FromMinutes(5));

        afterwards.Should().BeEmpty();
        (await context.Service.GetAll()).Should().ContainSingle(candidate => candidate.Id == job.Id);
    }

    // Testzweck: Ein unbekannter Auftrag ist kein Serverfehler; er ist meist schon erledigt.
    [Test]
    public async Task Complete_ShouldReportNotFound_ForAnUnknownJob()
    {
        var context = new JobTestContext();

        var result = await context.Service.Complete(Guid.NewGuid(), "worker-a", null, Guid.NewGuid());

        result.Should().Be(JobOperationResult.NotFound);
    }

    private sealed class JobTestContext
    {
        public JobTestContext()
        {
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
            Storage = new InMemoryServiceTaskStorage();
            var provider = new SingleStorageProvider(Storage);
            Service = new ServiceTaskJobService(
                provider,
                new BpmnBusinessLogic(provider),
                Time,
                NullLogger<ServiceTaskJobService>.Instance);
        }

        public FakeTimeProvider Time { get; }
        public InMemoryServiceTaskStorage Storage { get; }
        public ServiceTaskJobService Service { get; }

        public async Task<ServiceTaskJob> AddJob(string type, int retries = 3)
        {
            var serviceTask = new BPMN.Activities.ServiceTask { Id = "ServiceTask_1", Name = type, Implementation = type };
            var job = new ServiceTaskJob
            {
                Id = Guid.NewGuid(),
                Type = type,
                Name = type,
                Token = new Token
                {
                    ProcessInstanceId = Guid.NewGuid(),
                    CurrentBaseElement = serviceTask,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                ProcessInstanceId = Guid.NewGuid(),
                MetaDefinitionId = "catalog",
                DefinitionId = Guid.NewGuid(),
                ProcessId = "Process_1",
                Retries = retries,
                CreatedAt = Time.GetUtcNow().UtcDateTime
            };

            await Storage.SaveJob(job);
            return job;
        }
    }

    /// <summary>Reicht dieselbe Ablage durch; die Auftragsvergabe braucht keine echte Transaktion.</summary>
    private sealed class SingleStorageProvider(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => new Wrapper(serviceTaskStorage);

        private sealed class Wrapper(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorage
        {
            public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
            public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
            public IInstanceStorage InstanceStorage => throw new NotSupportedException();
            public IFormStorage FormStorage => throw new NotSupportedException();
            public IServiceTaskStorage ServiceTaskStorage { get; } = serviceTaskStorage;

            public void CommitChanges()
            {
            }

            public void RollbackTransaction()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
