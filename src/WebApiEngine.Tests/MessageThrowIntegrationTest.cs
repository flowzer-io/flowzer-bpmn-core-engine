using System.Dynamic;
using FilesystemStorageSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Ein Prozess teilt einem anderen etwas mit: Die Engine sammelt die ausgehende Nachricht, die
/// Geschäftslogik stellt sie in derselben Transaktion über denselben Weg zu wie
/// <c>POST /message</c> — an eine wartende Instanz oder an ein Message-Start-Event.
/// </summary>
[NonParallelizable]
public sealed class MessageThrowIntegrationTest
{
    private const string SenderDefinitionId = "Definitions_Sender";
    private const string ReceiverDefinitionId = "Definitions_Receiver";

    // Testzweck: Instanz A wirft, die wartende Instanz B bekommt die Nachricht samt der
    // gemappten Variablen und läuft weiter; A wartet unterdessen an ihrem nächsten Schritt.
    [Test]
    public async Task Throw_ShouldContinueAnotherWaitingInstanceWithTheSentVariables()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, ReceiverDefinitionId, ReceiverXml(instantiate: false));
        await DeployAsync(provider, engine, SenderDefinitionId, SenderXml());

        var receiver = await engine.StartProcessInstance(ReceiverDefinitionId, Variables(("antragsnummer", "4711")));
        var sender = await engine.StartProcessInstance(SenderDefinitionId,
            Variables(("antragsnummer", "4711"), ("entscheidung", "genehmigt"), ("intern", "bleibt hier")));

        using (new AssertionScope())
        {
            // B ist weitergelaufen und wartet jetzt hinter dem Empfang.
            var continued = await InstanceAsync(provider, receiver.InstanceId);
            ActiveNodeIds(continued).Should().BeEquivalentTo(["NachDemEmpfang"]);
            ProcessVariables(continued).Should().Contain("entscheidung", "genehmigt");
            ProcessVariables(continued).Should().NotContainKey("intern");

            // A hat nicht auf eine Antwort gewartet, sondern ist sofort weitergelaufen.
            ActiveNodeIds(await InstanceAsync(provider, sender.InstanceId)).Should().BeEquivalentTo(["NachDemWurf"]);
        }
    }

    // Testzweck: Wartet niemand, verfällt die Nachricht — das werfende Element gilt trotzdem als
    // abgeschlossen und der sendende Prozess läuft weiter.
    [Test]
    public async Task Throw_ShouldContinueWhenNobodyIsWaiting()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, SenderDefinitionId, SenderXml());

        var sender = await engine.StartProcessInstance(SenderDefinitionId,
            Variables(("antragsnummer", "4711"), ("entscheidung", "genehmigt")));

        ActiveNodeIds(await InstanceAsync(provider, sender.InstanceId)).Should().BeEquivalentTo(["NachDemWurf"]);
    }

    // Testzweck: Wartet keine Instanz, startet die geworfene Nachricht über ein
    // Message-Start-Event eine neue Instanz des Empfängerprozesses.
    [Test]
    public async Task Throw_ShouldStartANewInstanceViaMessageStartEvent()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, ReceiverDefinitionId, ReceiverXml(instantiate: true));
        await DeployAsync(provider, engine, SenderDefinitionId, SenderXml());

        await engine.StartProcessInstance(SenderDefinitionId,
            Variables(("antragsnummer", "4711"), ("entscheidung", "genehmigt")));

        var started = (await InstancesAsync(provider))
            .Where(instance => instance.metaDefinitionId == ReceiverDefinitionId)
            .ToArray();

        using (new AssertionScope())
        {
            var receiver = started.Should().ContainSingle().Subject;
            ActiveNodeIds(receiver).Should().BeEquivalentTo(["NachDemStart"]);
            ProcessVariables(receiver).Should().Contain("entscheidung", "genehmigt");
        }
    }

    // Testzweck: Eine geworfene Nachricht erreicht auch den wartenden Parallelzweig derselben
    // Instanz; der Stand im Speicher läuft weiter, statt den gerade erreichten Fortschritt zu verlieren.
    [Test]
    public async Task Throw_ShouldCorrelateWithinTheSameInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, SenderDefinitionId, SelfCorrelatingXml());

        var instance = await engine.StartProcessInstance(SenderDefinitionId,
            Variables(("antragsnummer", "4711"), ("entscheidung", "genehmigt")));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, instance.InstanceId);
            ActiveNodeIds(stored).Should().BeEquivalentTo(["NachDemWurf", "NachDemEmpfang"]);
            ProcessVariables(stored).Should().Contain("entscheidung", "genehmigt");
        }
    }

    // Testzweck: Ein Send-Task mit Auftragstyp erzeugt einen Auftrag für einen externen Worker,
    // statt die Nachricht selbst zu korrelieren.
    [Test]
    public async Task SendTaskWithTaskDefinition_ShouldCreateAJobInsteadOfCorrelating()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, ReceiverDefinitionId, ReceiverXml(instantiate: true));
        await DeployAsync(provider, engine, SenderDefinitionId, WorkerSendTaskXml());

        var sender = await engine.StartProcessInstance(SenderDefinitionId,
            Variables(("antragsnummer", "4711"), ("entscheidung", "genehmigt")));

        using (var storage = provider.GetTransactionalStorage())
        {
            var job = (await storage.ServiceTaskStorage.GetJobs())
                .Should().ContainSingle().Subject;

            using (new AssertionScope())
            {
                job.Type.Should().Be("mail-versenden");
                job.FlowNodeId.Should().Be("Senden");
                job.ProcessInstanceId.Should().Be(sender.InstanceId);
                job.Retries.Should().Be(3);
            }
        }

        // Kein Empfänger ist gestartet worden: Der Auftragstyp ersetzt die interne Zustellung.
        (await InstancesAsync(provider)).Should().ContainSingle();
    }

    private static ExpandoObject Variables(params (string Key, object? Value)[] values)
    {
        var variables = new ExpandoObject();
        var entries = (IDictionary<string, object?>)variables;
        foreach (var (key, value) in values) entries[key] = value;

        return variables;
    }

    private static string[] ActiveNodeIds(ProcessInstanceInfo instance) => instance.Tokens
        .Where(token => token.State == FlowNodeState.Active && token.CurrentFlowNode is not null
            && token.ParentTokenId is not null)
        .Select(token => token.CurrentFlowNode!.Id)
        .ToArray();

    private static IDictionary<string, object?> ProcessVariables(ProcessInstanceInfo instance) =>
        (IDictionary<string, object?>?)instance.Tokens.Single(token => token.ParentTokenId is null).Variables
        ?? new Dictionary<string, object?>();

    private static async Task<ProcessInstanceInfo> InstanceAsync(ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.InstanceStorage.GetProcessInstance(instanceId);
    }

    private static async Task<ProcessInstanceInfo[]> InstancesAsync(ITransactionalStorageProvider provider)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.InstanceStorage.GetAllInstances()).ToArray();
    }

    private static async Task DeployAsync(
        ITransactionalStorageProvider provider,
        BpmnBusinessLogic engine,
        string metaDefinitionId,
        string xml)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = metaDefinitionId,
            Version = new Model.Version(1, 0),
            Hash = "test",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            IsActive = false
        };

        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = metaDefinitionId, Name = metaDefinitionId
            });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            storage.CommitChanges();
        }

        await engine.DeployDefinition(definition);
    }

    /// <summary>Wirft die Nachricht und wartet danach an einem eigenen Schritt weiter.</summary>
    private static string SenderXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Sender" targetNamespace="test">
          <bpmn:message id="Message_Freigabe" name="Freigabe erteilt">
            <bpmn:extensionElements><zeebe:subscription correlationKey="=antragsnummer" /></bpmn:extensionElements>
          </bpmn:message>
          <bpmn:process id="Process_Sender" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:intermediateThrowEvent id="Werfen">
              <bpmn:extensionElements><zeebe:ioMapping>
                <zeebe:input source="=antragsnummer" target="antragsnummer" />
                <zeebe:input source="=entscheidung" target="entscheidung" />
              </zeebe:ioMapping></bpmn:extensionElements>
              <bpmn:messageEventDefinition id="Definition_Werfen" messageRef="Message_Freigabe" />
            </bpmn:intermediateThrowEvent>
            <bpmn:serviceTask id="NachDemWurf"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Werfen" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Werfen" targetRef="NachDemWurf" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="NachDemWurf" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Wartet auf die Nachricht — entweder als laufende Instanz am Zwischenereignis oder,
    /// mit <paramref name="instantiate"/>, als Message-Start-Event einer neuen Instanz.
    /// </summary>
    private static string ReceiverXml(bool instantiate) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Receiver" targetNamespace="test">
          <bpmn:message id="Message_Freigabe" name="Freigabe erteilt">
            <bpmn:extensionElements><zeebe:subscription correlationKey="=antragsnummer" /></bpmn:extensionElements>
          </bpmn:message>
          <bpmn:process id="Process_Receiver" isExecutable="true">
        {{(instantiate ? """
            <bpmn:startEvent id="Start">
              <bpmn:messageEventDefinition id="Definition_Start" messageRef="Message_Freigabe" />
            </bpmn:startEvent>
            <bpmn:serviceTask id="NachDemStart"><bpmn:extensionElements>
              <zeebe:taskDefinition type="empfangen" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="NachDemStart" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="NachDemStart" targetRef="Ende" />
        """ : """
            <bpmn:startEvent id="Start" />
            <bpmn:intermediateCatchEvent id="Empfangen">
              <bpmn:messageEventDefinition id="Definition_Empfangen" messageRef="Message_Freigabe" />
            </bpmn:intermediateCatchEvent>
            <bpmn:serviceTask id="NachDemEmpfang"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Empfangen" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Empfangen" targetRef="NachDemEmpfang" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="NachDemEmpfang" targetRef="Ende" />
        """)}}
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Ein Zweig wirft, der parallele Zweig derselben Instanz wartet auf die Nachricht.</summary>
    private static string SelfCorrelatingXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Sender" targetNamespace="test">
          <bpmn:message id="Message_Freigabe" name="Freigabe erteilt">
            <bpmn:extensionElements><zeebe:subscription correlationKey="=antragsnummer" /></bpmn:extensionElements>
          </bpmn:message>
          <bpmn:process id="Process_Sender" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:parallelGateway id="Teilen" />
            <bpmn:intermediateCatchEvent id="Empfangen">
              <bpmn:messageEventDefinition id="Definition_Empfangen" messageRef="Message_Freigabe" />
            </bpmn:intermediateCatchEvent>
            <bpmn:serviceTask id="NachDemEmpfang"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:intermediateThrowEvent id="Werfen">
              <bpmn:extensionElements><zeebe:ioMapping>
                <zeebe:input source="=entscheidung" target="entscheidung" />
              </zeebe:ioMapping></bpmn:extensionElements>
              <bpmn:messageEventDefinition id="Definition_Werfen" messageRef="Message_Freigabe" />
            </bpmn:intermediateThrowEvent>
            <bpmn:serviceTask id="NachDemWurf"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="EndeEmpfang" />
            <bpmn:endEvent id="EndeWurf" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Teilen" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Teilen" targetRef="Empfangen" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="Empfangen" targetRef="NachDemEmpfang" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="NachDemEmpfang" targetRef="EndeEmpfang" />
            <bpmn:sequenceFlow id="Flow_5" sourceRef="Teilen" targetRef="Werfen" />
            <bpmn:sequenceFlow id="Flow_6" sourceRef="Werfen" targetRef="NachDemWurf" />
            <bpmn:sequenceFlow id="Flow_7" sourceRef="NachDemWurf" targetRef="EndeWurf" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Derselbe Versand, aber als Auftrag an einen externen Worker.</summary>
    private static string WorkerSendTaskXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Sender" targetNamespace="test">
          <bpmn:message id="Message_Freigabe" name="Freigabe erteilt">
            <bpmn:extensionElements><zeebe:subscription correlationKey="=antragsnummer" /></bpmn:extensionElements>
          </bpmn:message>
          <bpmn:process id="Process_Sender" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:sendTask id="Senden" messageRef="Message_Freigabe"><bpmn:extensionElements>
              <zeebe:taskDefinition type="mail-versenden" retries="3" />
            </bpmn:extensionElements></bpmn:sendTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Senden" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Senden" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
