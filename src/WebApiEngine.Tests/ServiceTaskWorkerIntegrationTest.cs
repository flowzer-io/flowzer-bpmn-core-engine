using FilesystemStorageSystem;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using Variables = System.Dynamic.ExpandoObject;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;

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

    // Testzweck: Der Auftrag traegt die Prozessvariablen. Sie liegen am Prozess-Token, nicht am
    // Token des Service-Tasks; wurde nur letzteres uebergeben, bekam der Worker einen leeren
    // Auftrag — eine Vertretungspruefung ohne den Namen der Vertretung.
    [Test]
    public async Task FetchedJob_ShouldCarryTheProcessVariables()
    {
        using var context = new WorkerContext();
        await context.DeployProcess("Definitions_Antrag", AntragProcessXml, "Antrag");

        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_Antrag");

        await context.CompleteFirstUserTask(instance.InstanceId, "UserTask_1", new Dictionary<string, object?>
        {
            ["vertretung"] = "Melli",
        });

        var job = (await context.JobService.GetAll()).Single();

        job.Variables.Should().NotBeNull();
        ((IDictionary<string, object?>)job.Variables!).Should().ContainKey("vertretung")
            .WhoseValue.Should().Be("Melli");
    }

    // Testzweck: Deklariert der Service-Task Eingaben, bekommt der Worker genau diese und nicht
    // den ganzen Prozess. Das ist der Weg, einem fremden Dienst nur das Noetige zu geben —
    // ohne diese Regel liefe jede Bemerkung aus dem Antrag mit hinaus.
    [Test]
    public async Task FetchedJob_ShouldCarryOnlyTheDeclaredInputs_WhenTheTaskDeclaresThem()
    {
        using var context = new WorkerContext();
        await context.DeployProcess("Definitions_Eingaben", MappedInputProcessXml, "Eingaben");

        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_Eingaben");
        await context.CompleteFirstUserTask(instance.InstanceId, "UserTask_1", new Dictionary<string, object?>
        {
            ["vertretung"] = "Melli",
            ["bemerkung"] = "gehoert niemanden sonst an",
        });

        var job = (await context.JobService.GetAll()).Single();
        var variables = (IDictionary<string, object?>)job.Variables!;

        variables.Should().ContainKey("nameDerVertretung").WhoseValue.Should().Be("Melli");
        variables.Should().NotContainKey("bemerkung");
    }

    // Testzweck: Der Weg, den eine Worker-Rueckmeldung wirklich nimmt — vom JSON des Workers
    // ueber die Ablage bis in die Bedingung am Tor. Genau hier ging der Wert verloren: Ohne
    // den ExpandoObjectConverter kam ein JsonElement an, die Ablage speicherte davon nur
    // {"ValueKind": 3}, die Bedingung war falsch und der Prozess nahm still den Standardfluss.
    // Ein Test, der nur das DTO deserialisiert, faengt das nicht ein.
    [Test]
    public async Task AWorkerResult_ShouldStillDecideTheGateway_AfterGoingThroughStorage()
    {
        using var context = new WorkerContext();
        await context.DeployProcess("Definitions_Tor", GatewayProcessXml, "Tor");

        var instance = await context.BusinessLogic.StartProcessInstance("Definitions_Tor");
        var job = (await context.JobService.FetchAndLock(
            "vertretung", WorkerUser, "worker-a", 10, TimeSpan.FromMinutes(5))).Single();

        // So kommt die Rueckmeldung tatsaechlich an: als JSON auf dem Endpunkt.
        var request = JsonSerializer.Deserialize<CompleteJobRequestDto>(
            """{ "workerId": "worker-a", "variables": { "vertretungFrei": "ja" } }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await context.JobService.Complete(job.Id, WorkerUser, request!.WorkerId, request.Variables);

        var finished = await context.GetInstance(instance.InstanceId);
        finished.Tokens.Select(token => token.CurrentFlowNode?.Id).Should().Contain("End_Ja",
            "die Antwort des Workers steuert das Tor; landet sie beschaedigt in der Ablage, "
            + "nimmt der Prozess still den Standardfluss");
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

        public Task DeployServiceProcess() => DeployProcess("Definitions_Service", ServiceProcessXml, "Zahlung");

        public async Task CompleteFirstUserTask(Guid instanceId, string flowNodeId, Dictionary<string, object?> data)
        {
            var engine = new core_engine.InstanceEngine((await GetInstance(instanceId)).Tokens)
            {
                InstanceId = instanceId,
            };
            var token = engine.GetActiveUserTasks().Single();

            Variables variables = new();
            var writable = (IDictionary<string, object?>)variables;
            foreach (var entry in data)
            {
                writable[entry.Key] = entry.Value;
            }

            await BusinessLogic.HandleUserTask(
                new UserTaskResult
                {
                    ProcessInstanceId = instanceId,
                    TokenId = token.Id,
                    FlowNodeId = flowNodeId,
                    Data = variables,
                },
                UserId);
        }

        public async Task<ProcessInstanceInfo> GetInstance(Guid instanceId)
        {
            using var storage = Provider.GetTransactionalStorage();
            return await storage.InstanceStorage.GetProcessInstance(instanceId);
        }

        public async Task DeployProcess(string definitionId, string xml, string name)
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = definitionId,
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
                    Name = name
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

    /// <summary>Antrag ausfuellen, danach ein Service-Task — der Worker braucht die Eingaben.</summary>
    private const string GatewayProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Tor" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Tor" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Vertretung pruefen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="vertretung" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="Gateway_1" />
            <bpmn:exclusiveGateway id="Gateway_1" default="Flow_Nein">
              <bpmn:incoming>Flow_2</bpmn:incoming>
              <bpmn:outgoing>Flow_Ja</bpmn:outgoing>
              <bpmn:outgoing>Flow_Nein</bpmn:outgoing>
            </bpmn:exclusiveGateway>
            <bpmn:sequenceFlow id="Flow_Ja" sourceRef="Gateway_1" targetRef="End_Ja">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=vertretungFrei = "ja"</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Nein" sourceRef="Gateway_1" targetRef="End_Nein" />
            <bpmn:endEvent id="End_Ja">
              <bpmn:incoming>Flow_Ja</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:endEvent id="End_Nein">
              <bpmn:incoming>Flow_Nein</bpmn:incoming>
            </bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string AntragProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Antrag" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Antrag" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />
            <bpmn:userTask id="UserTask_1" name="Antrag stellen">
              <bpmn:extensionElements>
                <zeebe:formDefinition formKey="Antrag" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Vertretung pruefen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="vertretung" retries="3" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_2</bpmn:incoming>
              <bpmn:outgoing>Flow_3</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="Flow_3" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_3</bpmn:incoming>
            </bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Wie AntragProcessXml, aber der Service-Task deklariert seine Eingaben.</summary>
    private const string MappedInputProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Eingaben" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Eingaben" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />
            <bpmn:userTask id="UserTask_1" name="Antrag stellen">
              <bpmn:extensionElements>
                <zeebe:formDefinition formKey="Antrag" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Vertretung pruefen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="vertretung" retries="3" />
                <zeebe:ioMapping>
                  <!-- Die Quelle ist ein Ausdruck; ohne fuehrendes = waere sie ein Festwert. -->
                  <zeebe:input source="=vertretung" target="nameDerVertretung" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_2</bpmn:incoming>
              <bpmn:outgoing>Flow_3</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="Flow_3" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_3</bpmn:incoming>
            </bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
