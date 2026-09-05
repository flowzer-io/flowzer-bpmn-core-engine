using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

/// <summary>
/// Der ganze Weg eines Service-Tasks: Er entsteht als Auftrag, ein externer Worker holt ihn,
/// meldet ein Ergebnis zurueck, und der Prozess laeuft weiter. Vorher war ein Service-Task ein
/// Wartezustand ohne jeden Ausweg.
/// </summary>
[NonParallelizable]
public class ServiceTaskWorkerIntegrationTest
{
    private static readonly Guid UserId = Guid.Parse("2D3B8F8E-1F84-4B7B-9E4E-6B1B1C2D3E4F");
    private static readonly Guid WorkerUser = UserId;

    // Testzweck: Eine gestartete Instanz mit Service-Task erzeugt genau einen Auftrag mit dem
    // im Modell angegebenen Typ.
    [Test]
    public async Task StartingAnInstance_ShouldCreateOneJobPerWaitingServiceTask()
    {
        using var context = new WorkerContext();
        await context.DeployServiceProcess();

        await context.BusinessLogic.StartProcessInstance("Definitions_Service");

        var jobs = await context.JobService.GetAll();
        jobs.Should().ContainSingle();
        jobs.Single().Type.Should().Be("zahlung");
        jobs.Single().ProcessId.Should().Be("Process_Service");
    }

    // Testzweck: Nach der Rueckmeldung des Workers laeuft die Instanz weiter und der Auftrag
    // verschwindet; sonst bliebe er als Karteileiche liegen.
    [Test]
    public async Task CompletingAJob_ShouldAdvanceTheInstanceAndRemoveTheJob()
    {
        using var context = new WorkerContext();
        await context.DeployServiceProcess();
        await context.BusinessLogic.StartProcessInstance("Definitions_Service");
        var job = (await context.JobService.FetchAndLock("zahlung", WorkerUser, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        var result = await context.JobService.Complete(job.Id, UserId, "worker-a", null);

        result.Should().Be(JobOperationResult.Ok);
        (await context.JobService.GetAll()).Should().BeEmpty();

        using var storage = context.Provider.GetTransactionalStorage();
        var instance = (await storage.InstanceStorage.GetAllInstances()).Single();
        instance.IsFinished.Should().BeTrue();
    }

    // Testzweck: Ein Auftrag darf nur einmal entstehen. Wird die Instanz zwischendurch erneut
    // gespeichert, behaelt ein arbeitender Worker seine Sperre.
    [Test]
    public async Task SavingTheInstanceAgain_ShouldNotDuplicateOrResetTheJob()
    {
        using var context = new WorkerContext();
        await context.DeployServiceProcess();
        await context.BusinessLogic.StartProcessInstance("Definitions_Service");
        var job = (await context.JobService.FetchAndLock("zahlung", WorkerUser, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        // Ein Timerdurchlauf speichert laufende Instanzen erneut.
        await context.BusinessLogic.HandleTime(DateTime.UtcNow);

        var jobs = await context.JobService.GetAll();
        jobs.Should().ContainSingle();
        jobs.Single().Id.Should().Be(job.Id);
        jobs.Single().LockedBy.Should().Be(ServiceTaskJobService.BuildLockOwner(WorkerUser, "worker-a"));
    }

    private sealed class WorkerContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public WorkerContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-worker-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);

            Provider = new FileSystemTransactionalStorageProvider();
            BusinessLogic = new BpmnBusinessLogic(Provider);
            JobService = new ServiceTaskJobService(
                Provider,
                BusinessLogic,
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                NullLogger<ServiceTaskJobService>.Instance);
        }

        public FileSystemTransactionalStorageProvider Provider { get; }
        public BpmnBusinessLogic BusinessLogic { get; }
        public ServiceTaskJobService JobService { get; }

        public async Task DeployServiceProcess()
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_Service",
                Hash = "hash",
                SavedByUser = UserId,
                SavedOn = DateTime.UtcNow,
                Version = new Model.Version(1, 0),
                IsActive = false
            };

            using (var storage = Provider.GetTransactionalStorage())
            {
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId,
                    Name = "Zahlung"
                });
                await storage.DefinitionStorage.StoreDefinition(definition);
                await storage.DefinitionStorage.StoreBinary(definition.Id, ServiceProcessXml);
            }

            await BusinessLogic.DeployDefinition(definition);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }

    private const string ServiceProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Service" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Service" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Zahlung ausloesen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="zahlung" retries="3" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
