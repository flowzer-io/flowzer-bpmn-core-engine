using System.Reflection;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
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

/// <summary>
/// Ein Worker meldet einen fachlichen Fehler statt eines Ergebnisses. Er laeuft danach auf
/// BPMN-Ebene weiter: Ein Error-Boundary-Event faengt ihn und der Prozess nimmt den Fehlerpfad;
/// ohne Faenger endet die Instanz als gescheitert, statt still liegen zu bleiben.
/// </summary>
[NonParallelizable]
public class ServiceTaskThrowErrorTest
{
    private static readonly Guid UserId = Guid.Parse("7C2B0E52-9A1D-4E67-9A3C-6F2B0E529A1D");

    // Testzweck: Mit gueltiger Lease greift das Error-Boundary am Service-Task; die Instanz
    // laeuft auf dem Fehlerpfad weiter und der erledigte Auftrag verschwindet.
    [Test]
    public async Task ThrowError_WithValidLease_ShouldFollowTheBoundaryOfTheServiceTask()
    {
        using var context = new ErrorWorkerContext();
        await context.DeployProcess(BoundaryProcessXml);
        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_ThrowError");
        var job = (await context.JobService.FetchAndLock("bonitaet", UserId, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        // So kommt die Meldung tatsaechlich an: als JSON auf dem Endpunkt.
        var request = JsonSerializer.Deserialize<ThrowJobErrorRequestDto>(
            """{ "workerId": "worker-a", "errorCode": "BONITAET", "errorMessage": "Score zu niedrig", "variables": { "grund": "Score" } }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = await context.JobService.ThrowError(
            job.Id, UserId, request.WorkerId, request.ErrorCode, request.ErrorMessage, request.Variables);

        result.Should().Be(JobOperationResult.Ok);
        var stored = await context.GetInstance(instance.InstanceId);
        stored.State.Should().Be(ProcessInstanceState.Waiting);
        stored.FailureReason.Should().BeNull();
        stored.Tokens.Single(token => token.CurrentFlowNode?.Id == "ServiceTask_1")
            .State.Should().Be(FlowNodeState.Withdrawn);

        // Der Folgepfad des Boundary laeuft: Es wartet jetzt der Auftrag der Nacharbeit.
        var openJobs = await context.JobService.GetAll();
        openJobs.Should().ContainSingle().Which.Type.Should().Be("nacharbeit");
        openJobs.Should().NotContain(open => open.Id == job.Id);
    }

    // Testzweck: Ohne passendes Boundary scheitert die Instanz mit einer Begruendung, die den
    // unbehandelten Fehlercode benennt.
    [Test]
    public async Task ThrowError_WithoutMatchingBoundary_ShouldFailTheInstance()
    {
        using var context = new ErrorWorkerContext();
        await context.DeployProcess(PlainProcessXml);
        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_ThrowError");
        var job = (await context.JobService.FetchAndLock("bonitaet", UserId, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        var result = await context.JobService.ThrowError(job.Id, UserId, "worker-a", "BONITAET", "Score zu niedrig", null);

        result.Should().Be(JobOperationResult.Ok);
        var stored = await context.GetInstance(instance.InstanceId);
        stored.State.Should().Be(ProcessInstanceState.Failed);
        stored.FailureReason.Should().Be("Unhandled BPMN error 'BONITAET' at 'ServiceTask_1'. Score zu niedrig");
        (await context.JobService.GetAll()).Should().BeEmpty();
    }

    // Testzweck: Das Laufzeitdiagramm braucht keine eigene Fehlerdarstellung. Der unterbrochene
    // Service-Task, das gefangene Boundary und der Folgepfad stehen bereits im vorhandenen
    // Zustandsmechanismus — Tokenstand und Ereignisspur —, aus dem die Projektion ihre Marker
    // bildet: Withdrawn wird "abgebrochen", Completed "abgeschlossen", Active "aktiv".
    [Test]
    public async Task ThrowError_ShouldMakeTheBoundaryPathVisibleInTheRuntimeState()
    {
        using var context = new ErrorWorkerContext();
        await context.DeployProcess(BoundaryProcessXml);
        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_ThrowError");
        var job = (await context.JobService.FetchAndLock("bonitaet", UserId, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        await context.JobService.ThrowError(job.Id, UserId, "worker-a", "BONITAET", null, null);

        var stored = await context.GetInstance(instance.InstanceId);
        var statesByNode = stored.Tokens
            .Where(token => token.CurrentFlowNode is not null)
            .ToDictionary(token => token.CurrentFlowNode!.Id, token => token.State);
        statesByNode["ServiceTask_1"].Should().Be(FlowNodeState.Withdrawn);
        statesByNode["BoundaryError_1"].Should().Be(FlowNodeState.Completed);
        statesByNode["ServiceTask_Recover"].Should().Be(FlowNodeState.Active);

        using var storage = context.Provider.GetTransactionalStorage();
        var events = await storage.RuntimeNodeEventStorage.GetByProcessInstance(instance.InstanceId);
        events.Should().Contain(item => item.FlowNodeId == "ServiceTask_1" && item.State == FlowNodeState.Withdrawn);
        events.Should().Contain(item => item.FlowNodeId == "BoundaryError_1" && item.State == FlowNodeState.Completed);
        events.Should().Contain(item => item.FlowNodeId == "ServiceTask_Recover" && item.State == FlowNodeState.Active);
    }

    // Testzweck: Eine abgelaufene Lease aendert nichts und erscheint als Konflikt — genau wie
    // beim Abschluss, damit ein zweiter Worker nicht ueberschrieben wird.
    [Test]
    public async Task ThrowError_WithExpiredLease_ShouldReportAConflict()
    {
        var context = new ThrowErrorControllerContext();
        var job = await context.AddAndClaimJob();
        context.Time.Advance(TimeSpan.FromMinutes(6));

        var action = await context.Controller.ThrowJobError(job.Id, new ThrowJobErrorRequestDto
        {
            WorkerId = "worker-a",
            ErrorCode = "BONITAET"
        });

        var response = action.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        response.StatusCode.Should().Be(409);
    }

    // Testzweck: Ein fehlender oder leerer Fehlercode ist keine Fehlermeldung, sondern eine
    // unvollstaendige Anfrage — und wird als Problem Details mit 400 abgelehnt.
    [TestCase("")]
    [TestCase("   ")]
    public async Task ThrowError_WithoutErrorCode_ShouldAnswerWithBadRequest(string errorCode)
    {
        var context = new ThrowErrorControllerContext();
        var job = await context.AddAndClaimJob();

        var action = await context.Controller.ThrowJobError(job.Id, new ThrowJobErrorRequestDto
        {
            WorkerId = "worker-a",
            ErrorCode = errorCode
        });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(400);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    // Testzweck: Der Endpunkt gehoert zur Worker-Rolle. Ein Auftrag traegt Prozessdaten; wer
    // ihn fachlich abschliessen darf, ist dieselbe enge Rolle wie beim Abholen.
    [Test]
    public void ThrowError_ShouldRequireTheWorkerRole()
    {
        var method = typeof(JobController).GetMethod(nameof(JobController.ThrowJobError))!;

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull("der Endpunkt darf die Klassenregel nicht lockern");
        typeof(JobController).GetCustomAttribute<AuthorizeAttribute>()!.Policy
            .Should().Be(FlowzerPolicies.Worker);
    }

    private sealed class ThrowErrorControllerContext
    {
        private readonly InMemoryServiceTaskStorage _storage = new();
        private readonly ServiceTaskJobService _service;

        public ThrowErrorControllerContext()
        {
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
            var provider = new ServiceTaskOnlyStorageProvider(_storage);
            _service = new ServiceTaskJobService(
                provider,
                new BpmnBusinessLogic(provider),
                Time,
                NullLogger<ServiceTaskJobService>.Instance);
            Controller = new JobController(
                _service,
                new ServiceTaskWebhookService(provider, new FlowzerWebhookOptions(), Time),
                new WorkerUserContextAccessor());
        }

        public FakeTimeProvider Time { get; }
        public JobController Controller { get; }

        public async Task<ServiceTaskJob> AddAndClaimJob()
        {
            var job = new ServiceTaskJob
            {
                Id = Guid.NewGuid(),
                Type = "bonitaet",
                Name = "Bonitaet pruefen",
                TokenId = Guid.NewGuid(),
                FlowNodeId = "ServiceTask_1",
                ProcessInstanceId = Guid.NewGuid(),
                MetaDefinitionId = "Definitions_ThrowError",
                DefinitionId = Guid.NewGuid(),
                ProcessId = "Process_ThrowError",
                CreatedAt = Time.GetUtcNow().UtcDateTime,
                Retries = 3
            };
            await _storage.SaveJob(job);
            return (await _service.FetchAndLock(job.Type, UserId, "worker-a", 1, TimeSpan.FromMinutes(5))).Single();
        }
    }

    private sealed class WorkerUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(UserId, "test", false);
    }

    private sealed class ServiceTaskOnlyStorageProvider(IServiceTaskStorage serviceTaskStorage)
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

    private sealed class ErrorWorkerContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public ErrorWorkerContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-throw-error-test", Guid.NewGuid().ToString("N"));
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

        public async Task<ProcessInstanceInfo> GetInstance(Guid instanceId)
        {
            using var storage = Provider.GetTransactionalStorage();
            return await storage.InstanceStorage.GetProcessInstance(instanceId);
        }

        public async Task DeployProcess(string xml)
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_ThrowError",
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
                    Name = "Bonitaet"
                });
                await storage.DefinitionStorage.StoreDefinition(definition);
                await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
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

    private const string BoundaryProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_ThrowError" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:error id="Error_Bonitaet" name="Bonitaet nicht ausreichend" errorCode="BONITAET" />
          <bpmn:process id="Process_ThrowError" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:serviceTask id="ServiceTask_1" name="Bonitaet pruefen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="bonitaet" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:boundaryEvent id="BoundaryError_1" name="Fehler" attachedToRef="ServiceTask_1">
              <bpmn:outgoing>Flow_3</bpmn:outgoing>
              <bpmn:errorEventDefinition id="ErrorEventDefinition_1" errorRef="Error_Bonitaet" />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="ServiceTask_Recover" name="Nacharbeit">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="nacharbeit" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_3</bpmn:incoming>
              <bpmn:outgoing>Flow_4</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:endEvent id="EndEvent_Recovered">
              <bpmn:incoming>Flow_4</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="BoundaryError_1" targetRef="ServiceTask_Recover" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="ServiceTask_Recover" targetRef="EndEvent_Recovered" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string PlainProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_ThrowError" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_ThrowError" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:serviceTask id="ServiceTask_1" name="Bonitaet pruefen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="bonitaet" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
