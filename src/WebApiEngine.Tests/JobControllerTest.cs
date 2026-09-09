using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Controller;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>HTTP-Randvertrag fuer Heartbeats lang laufender Service-Task-Worker.</summary>
public sealed class JobControllerTest
{
    private static readonly Guid WorkerUser = Guid.Parse("A1B2C3D4-0000-4000-8000-000000000001");

    // Testzweck: Der Heartbeat antwortet mit dem serverseitig gespeicherten Ablaufzeitpunkt,
    // damit ein Worker den naechsten Heartbeat nicht aus seiner lokalen Uhr ableiten muss.
    [Test]
    public async Task RenewJobLease_ShouldReturnTheStoredExpiry()
    {
        var context = new ControllerTestContext();
        var job = await context.AddAndClaimJob();

        var action = await context.Controller.RenewJobLease(job.Id, new RenewJobLeaseRequestDto
        {
            WorkerId = "worker-a",
            LockSeconds = 600
        });

        var response = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = response.Value.Should()
            .BeOfType<ApiStatusResult<RenewJobLeaseResultDto>>().Subject;
        payload.Successful.Should().BeTrue();
        payload.Result.Should().Be(new RenewJobLeaseResultDto
        {
            JobId = job.Id,
            LockedUntil = context.Time.GetUtcNow().AddMinutes(10)
        });
    }

    // Testzweck: Fehlende Worker-Kennung und Lease-Dauern ausserhalb des oeffentlichen Limits
    // werden vor jedem Storage-Zugriff als 400 abgelehnt.
    [TestCase("", 300)]
    [TestCase("worker-a", 0)]
    [TestCase("worker-a", 3601)]
    public async Task RenewJobLease_ShouldRejectInvalidRequests(string workerId, int lockSeconds)
    {
        var controller = new JobController(null!, null!, null!);

        var action = await controller.RenewJobLease(Guid.NewGuid(), new RenewJobLeaseRequestDto
        {
            WorkerId = workerId,
            LockSeconds = lockSeconds
        });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(400);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    // Testzweck: Eine abgelaufene Lease erscheint am neuen API-Rand als standardisiertes
    // Problem Details mit 409 und nicht als erfolgreicher Legacy-Umschlag.
    [Test]
    public async Task RenewJobLease_ShouldReturnProblemDetailsForAnExpiredLease()
    {
        var context = new ControllerTestContext();
        var job = await context.AddAndClaimJob();
        context.Time.Advance(TimeSpan.FromMinutes(6));

        var action = await context.Controller.RenewJobLease(job.Id, new RenewJobLeaseRequestDto
        {
            WorkerId = "worker-a",
            LockSeconds = 300
        });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(409);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    private sealed class ControllerTestContext
    {
        private readonly InMemoryServiceTaskStorage _storage = new();
        private readonly ServiceTaskJobService _service;

        public ControllerTestContext()
        {
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
            var provider = new SingleStorageProvider(_storage);
            _service = new ServiceTaskJobService(
                provider,
                new BpmnBusinessLogic(provider),
                Time,
                NullLogger<ServiceTaskJobService>.Instance);
            Controller = new JobController(
                _service,
                new ServiceTaskWebhookService(provider, new FlowzerWebhookOptions(), Time),
                new FixedCurrentUserContextAccessor());
        }

        public FakeTimeProvider Time { get; }
        public JobController Controller { get; }

        public async Task<ServiceTaskJob> AddAndClaimJob()
        {
            var job = new ServiceTaskJob
            {
                Id = Guid.NewGuid(),
                Type = "flowzer.ai",
                Name = "KI-Auftrag",
                TokenId = Guid.NewGuid(),
                FlowNodeId = "ServiceTask_AI",
                ProcessInstanceId = Guid.NewGuid(),
                MetaDefinitionId = "ai-demo",
                DefinitionId = Guid.NewGuid(),
                ProcessId = "Process_AI",
                CreatedAt = Time.GetUtcNow().UtcDateTime,
                Retries = 3
            };
            await _storage.SaveJob(job);
            return (await _service.FetchAndLock(
                job.Type,
                WorkerUser,
                "worker-a",
                1,
                TimeSpan.FromMinutes(5))).Single();
        }
    }

    private sealed class FixedCurrentUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(WorkerUser, "test", false);
    }

    private sealed class SingleStorageProvider(IServiceTaskStorage serviceTaskStorage)
        : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => new Wrapper(serviceTaskStorage);

        private sealed class Wrapper(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorage
        {
            public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
            public IFolderStorage FolderStorage => throw new NotSupportedException();
            public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
            public IInstanceStorage InstanceStorage => throw new NotSupportedException();
            public IFormStorage FormStorage => throw new NotSupportedException();
            public IServiceTaskStorage ServiceTaskStorage { get; } = serviceTaskStorage;
            public void CommitChanges() { }
            public void RollbackTransaction() { }
            public void Dispose() { }
        }
    }
}
