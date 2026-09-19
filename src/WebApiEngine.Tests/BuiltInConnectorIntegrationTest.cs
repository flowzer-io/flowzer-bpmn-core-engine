using System.Net;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Connectors;
using WebApiEngine.Jobs;
using Model;
using StorageSystem;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Der ganze Weg eines mitgelieferten Konnektors: vom Service-Task im Modell ueber den
/// Auftrag, den eingebauten Host und den Aufruf bis zurueck in den Prozesskontext. Erst hier
/// zeigt sich, dass ein Ergebnis die Ablage unbeschaedigt uebersteht und ein Error-Boundary
/// den fachlichen Ausgang tatsaechlich faengt.
/// </summary>
[NonParallelizable]
public class BuiltInConnectorIntegrationTest
{
    private static readonly Guid Deployer = Guid.Parse("7C2B0E52-9A1D-4E67-9A3C-6F2B0E529A1D");

    // Testzweck: Der Erfolgsfall fuehrt den Prozess weiter und legt Status und geparsten
    // JSON-Koerper als lesbare Werte in den Vorgang. Waere der Koerper nur Text, koennte ein
    // folgender Schritt kein einzelnes Feld auswerten.
    [Test]
    public async Task HttpConnector_ShouldAdvanceTheProcessAndStoreTheParsedBody()
    {
        using var context = new ConnectorEngineContext(_ =>
            RecordingConnectorHandler.Json("""{ "belegNummer": "R-2026-118" }"""));
        await context.Deploy(BoundaryProcessXml);
        var started = await context.Start(ConnectorTestData.AllowedUrl);

        await context.RunConnectorOnce();

        var instance = await context.GetInstance(started.InstanceId);
        instance.State.Should().Be(ProcessInstanceState.Completed);
        var master = (IDictionary<string, object?>)instance.Tokens
            .Single(token => token.ParentTokenId is null).Variables!;
        master["status"].Should().Be(200);
        ((IDictionary<string, object?>)master["body"]!)["belegNummer"].Should().Be("R-2026-118");
        context.Diagnostics.GetSnapshot().Single(connector => connector.Name == "http")
            .ProcessedJobs.Should().Be(1);
    }

    // Testzweck: Ein nicht freigegebenes Ziel ist ein fachlicher Ausgang mit eigenem Weg. Das
    // Error-Boundary am Service-Task faengt `HTTP_NOT_ALLOWED`, und der Prozess scheitert nicht.
    [Test]
    public async Task HttpConnector_ShouldLetTheBoundaryCatchADisallowedTarget()
    {
        using var context = new ConnectorEngineContext(_ => RecordingConnectorHandler.Json("{}"));
        await context.Deploy(BoundaryProcessXml);
        var started = await context.Start("https://boeser.example/hook");

        await context.RunConnectorOnce();

        var instance = await context.GetInstance(started.InstanceId);
        instance.State.Should().Be(ProcessInstanceState.Waiting);
        instance.FailureReason.Should().BeNull();
        instance.Tokens.Single(token => token.CurrentFlowNode?.Id == "ServiceTask_1")
            .State.Should().Be(FlowNodeState.Withdrawn);
        instance.Tokens.Should().Contain(token =>
            token.CurrentFlowNode != null && token.CurrentFlowNode.Id == "ServiceTask_Klaeren"
            && token.State == FlowNodeState.Active);
    }

    // Testzweck: Ein 404 wird zu `HTTP_404`. Ein eigenes Boundary fuer genau diesen Code nimmt
    // den Fehlerpfad — so wird aus einer fremden Antwort eine Entscheidung im Modell.
    [Test]
    public async Task HttpConnector_ShouldLetADedicatedBoundaryCatchHttp404()
    {
        using var context = new ConnectorEngineContext(_ =>
            RecordingConnectorHandler.Text("nicht gefunden", HttpStatusCode.NotFound));
        await context.Deploy(NotFoundBoundaryProcessXml);
        var started = await context.Start(ConnectorTestData.AllowedUrl);

        await context.RunConnectorOnce();

        var instance = await context.GetInstance(started.InstanceId);
        instance.State.Should().Be(ProcessInstanceState.Waiting);
        instance.Tokens.Should().Contain(token =>
            token.CurrentFlowNode != null && token.CurrentFlowNode.Id == "ServiceTask_Klaeren"
            && token.State == FlowNodeState.Active);
    }

    // Testzweck: Ein Serverfehler ist technisch und darf den Prozess nicht auf den Fehlerpfad
    // schicken: Der Auftrag bleibt liegen, verliert einen Versuch und wartet auf die Wartezeit.
    [Test]
    public async Task HttpConnector_ShouldOnlyFailTheJob_OnAServerError()
    {
        using var context = new ConnectorEngineContext(_ =>
            RecordingConnectorHandler.Text("boom", HttpStatusCode.InternalServerError));
        await context.Deploy(BoundaryProcessXml);
        var started = await context.Start(ConnectorTestData.AllowedUrl);

        await context.RunConnectorOnce();

        var instance = await context.GetInstance(started.InstanceId);
        instance.State.Should().Be(ProcessInstanceState.Waiting);
        var job = (await context.JobService.GetAll()).Should().ContainSingle().Subject;
        job.Retries.Should().Be(2);
        job.RetryAt.Should().NotBeNull();
        job.LockedBy.Should().BeNull();
        context.Diagnostics.GetSnapshot().Single(connector => connector.Name == "http")
            .FailedJobs.Should().Be(1);
    }

    private sealed class ConnectorEngineContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;
        private readonly HttpConnector _connector;
        private readonly BuiltInConnectorBackgroundService _host;

        public ConnectorEngineContext(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-connector-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);

            Provider = new FileSystemTransactionalStorageProvider();
            BusinessLogic = new BpmnBusinessLogic(Provider);
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
            JobService = new ServiceTaskJobService(
                Provider,
                BusinessLogic,
                time,
                NullLogger<ServiceTaskJobService>.Instance);

            var options = new FlowzerConnectorOptions
            {
                Http = new HttpConnectorOptions
                {
                    Enabled = true,
                    AllowedHosts = [ConnectorTestData.AllowedHost]
                }
            };
            _connector = new HttpConnector(
                new SingleHandlerHttpClientFactory(new RecordingConnectorHandler(respond)),
                options,
                new ConnectorSecretResolver(options),
                NullLogger<HttpConnector>.Instance);
            _host = new BuiltInConnectorBackgroundService(
                [_connector],
                JobService,
                options,
                Diagnostics,
                time,
                NullLogger<BuiltInConnectorBackgroundService>.Instance);
        }

        public FileSystemTransactionalStorageProvider Provider { get; }
        public BpmnBusinessLogic BusinessLogic { get; }
        public ServiceTaskJobService JobService { get; }
        public ConnectorDiagnosticsState Diagnostics { get; } = new();

        public Task RunConnectorOnce() => _host.RunOnce(_connector, CancellationToken.None);

        public Task<ProcessInstanceInfo> Start(string url)
        {
            Variables variables = new();
            ((IDictionary<string, object?>)variables)["zielAdresse"] = url;
            return BusinessLogic.StartProcessInstance("Definitions_Connector", variables);
        }

        public async Task<ProcessInstanceInfo> GetInstance(Guid instanceId)
        {
            using var storage = Provider.GetTransactionalStorage();
            return await storage.InstanceStorage.GetProcessInstance(instanceId);
        }

        public async Task Deploy(string xml)
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_Connector",
                Hash = "hash",
                SavedByUser = Deployer,
                SavedOn = DateTime.UtcNow,
                Version = new Model.Version(1, 0),
                IsActive = false
            };

            using (var storage = Provider.GetTransactionalStorage())
            {
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId,
                    Name = "Konnektor"
                });
                await storage.DefinitionStorage.StoreDefinition(definition);
                await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            }

            await BusinessLogic.DeployDefinition(definition);
        }

        public void Dispose()
        {
            _host.Dispose();
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
                          id="Definitions_Connector" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:error id="Error_NotAllowed" name="Ziel nicht erlaubt" errorCode="HTTP_NOT_ALLOWED" />
          <bpmn:process id="Process_Connector" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:serviceTask id="ServiceTask_1" name="Beleg abrufen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer:http" retries="3" />
                <zeebe:ioMapping>
                  <zeebe:input source="=zielAdresse" target="url" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:boundaryEvent id="BoundaryError_1" name="Nicht erlaubt" attachedToRef="ServiceTask_1">
              <bpmn:outgoing>Flow_3</bpmn:outgoing>
              <bpmn:errorEventDefinition id="ErrorEventDefinition_1" errorRef="Error_NotAllowed" />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="ServiceTask_Klaeren" name="Ziel klaeren">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="nacharbeit" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_3</bpmn:incoming>
              <bpmn:outgoing>Flow_4</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:endEvent id="EndEvent_Geklaert">
              <bpmn:incoming>Flow_4</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="BoundaryError_1" targetRef="ServiceTask_Klaeren" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="ServiceTask_Klaeren" targetRef="EndEvent_Geklaert" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string NotFoundBoundaryProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Connector" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:error id="Error_NotFound" name="Beleg unbekannt" errorCode="HTTP_404" />
          <bpmn:process id="Process_Connector" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:serviceTask id="ServiceTask_1" name="Beleg abrufen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer:http" retries="3" />
                <zeebe:ioMapping>
                  <zeebe:input source="=zielAdresse" target="url" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:boundaryEvent id="BoundaryError_1" name="Unbekannt" attachedToRef="ServiceTask_1">
              <bpmn:outgoing>Flow_3</bpmn:outgoing>
              <bpmn:errorEventDefinition id="ErrorEventDefinition_1" errorRef="Error_NotFound" />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="ServiceTask_Klaeren" name="Beleg klaeren">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="nacharbeit" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_3</bpmn:incoming>
              <bpmn:outgoing>Flow_4</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:endEvent id="EndEvent_Geklaert">
              <bpmn:incoming>Flow_4</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="BoundaryError_1" targetRef="ServiceTask_Klaeren" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="ServiceTask_Klaeren" targetRef="EndEvent_Geklaert" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
