using System.Dynamic;
using FilesystemStorageSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Der Laufzeitpfad von Vertrag 9 über die Geschäftslogik: ereignisbasiertes Gateway,
/// Event-Subprozess und inklusives Gateway. Geprüft wird nicht nur der Tokenstand, sondern
/// auch, was davon als Subscription in der Ablage steht — daran hängt, ob eine Nachricht oder
/// ein Timer die Instanz später überhaupt erreicht.
/// </summary>
[NonParallelizable]
public sealed class GatewayAndEventSubProcessIntegrationTest
{
    private const string EventGatewayDefinitionId = "Definitions_EventGateway";
    private const string EventSubProcessDefinitionId = "Definitions_EventSubProcess";
    private const string InclusiveDefinitionId = "Definitions_Inclusive";

    // Testzweck: Hinter dem ereignisbasierten Gateway stehen Nachricht und Timer gleichzeitig als
    // Subscription in der Ablage; trifft die Nachricht ein, verschwinden beide und nur ihr
    // Folgepfad läuft weiter.
    [Test]
    public async Task EventBasedGateway_ShouldStoreBothSubscriptionsAndKeepOnlyTheWinningBranch()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, EventGatewayDefinitionId, EventBasedGatewayXml());

        var instance = await engine.StartProcessInstance(EventGatewayDefinitionId, Variables());

        using (new AssertionScope())
        {
            (await MessageSubscriptionNamesAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["Freigabe erteilt"]);
            (await TimerFlowNodeIdsAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["Frist"]);
        }

        await engine.HandleMessage(new Message { Name = "Freigabe erteilt", InstanceId = instance.InstanceId });

        using (new AssertionScope())
        {
            ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["NachFreigabe"]);
            (await MessageSubscriptionNamesAsync(provider, instance.InstanceId)).Should().BeEmpty();
            (await TimerFlowNodeIdsAsync(provider, instance.InstanceId)).Should().BeEmpty();
        }
    }

    // Testzweck: Das Startereignis eines Event-Subprozesses steht als Nachrichten-Subscription des
    // laufenden Prozesses in der Ablage; die Nachricht unterbricht den Scope und startet ihn.
    [Test]
    public async Task EventSubProcess_ShouldBeReachableThroughAStoredMessageSubscription()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, EventSubProcessDefinitionId, EventSubProcessXml());

        var instance = await engine.StartProcessInstance(EventSubProcessDefinitionId, Variables());

        using (new AssertionScope())
        {
            ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["Bearbeiten"]);
            (await MessageSubscriptionNamesAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["Abbruch"]);
        }

        await engine.HandleMessage(new Message { Name = "Abbruch", InstanceId = instance.InstanceId });

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, instance.InstanceId);
            // Der Event-Subprozess selbst ist der laufende Scope seines Inhalts.
            ActiveNodeIds(stored).Should().BeEquivalentTo(["EventSub", "Abbrechen"]);
            TokenState(stored, "Bearbeiten").Should().Be(FlowNodeState.Withdrawn);
            // Ein unterbrechender Start ist danach nicht mehr scharf.
            (await MessageSubscriptionNamesAsync(provider, instance.InstanceId)).Should().BeEmpty();
        }
    }

    // Testzweck: Der Abschluss einer Aufgabe entscheidet mit seinen Variablen, welche Zweige des
    // inklusiven Gateways laufen; der Join wartet auf genau diese und nicht auf mehr.
    [Test]
    public async Task InclusiveGateway_ShouldBranchOnTaskResultVariablesAndJoinOnThem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, InclusiveDefinitionId, InclusiveGatewayXml());

        var instance = await engine.StartProcessInstance(InclusiveDefinitionId, Variables());

        await CompleteJobAsync(provider, engine, "entscheiden", Variables(("wegA", true), ("wegB", true)));

        using (new AssertionScope())
        {
            ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
                .Should().BeEquivalentTo(["WegA", "WegB"]);
            (await InstanceAsync(provider, instance.InstanceId)).Tokens
                .Should().NotContain(token => token.CurrentFlowNode != null && token.CurrentFlowNode.Id == "Standard");
        }

        await CompleteJobAsync(provider, engine, "wegA", null);

        // Der Join hat noch nicht ausgeloest: Sein erstes Token wartet dort, waehrend der zweite
        // aktivierte Zweig noch laeuft.
        ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
            .Should().BeEquivalentTo(["WegB", "Join"]);

        await CompleteJobAsync(provider, engine, "wegB", null);

        (await InstanceAsync(provider, instance.InstanceId)).State
            .Should().Be(ProcessInstanceState.Completed);
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

    private static FlowNodeState TokenState(ProcessInstanceInfo instance, string flowNodeId) => instance.Tokens
        .Single(token => token.CurrentFlowNode is not null && token.CurrentFlowNode.Id == flowNodeId).State;

    private static async Task<ProcessInstanceInfo> InstanceAsync(ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.InstanceStorage.GetProcessInstance(instanceId);
    }

    private static async Task<string[]> MessageSubscriptionNamesAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.SubscriptionStorage.GetMessageSubscription(instanceId))
            .Select(subscription => subscription.Message.Name)
            .ToArray();
    }

    private static async Task<string[]> TimerFlowNodeIdsAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.SubscriptionStorage.GetTimerSubscriptions(instanceId))
            .Select(subscription => subscription.FlowNodeId)
            .ToArray();
    }

    private static async Task CompleteJobAsync(
        ITransactionalStorageProvider provider,
        BpmnBusinessLogic engine,
        string jobType,
        ExpandoObject? result)
    {
        ServiceTaskJob job;
        using (var storage = provider.GetTransactionalStorage())
        {
            job = (await storage.ServiceTaskStorage.GetJobsByType(jobType)).Single();
        }

        await engine.CompleteServiceTaskJob(job, result, Guid.NewGuid());
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

    /// <summary>Freigabe oder Frist — was zuerst eintrifft, entscheidet.</summary>
    private static string EventBasedGatewayXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            id="Definitions_EventGateway" targetNamespace="test">
          <bpmn:message id="Message_Freigabe" name="Freigabe erteilt" />
          <bpmn:process id="Process_EventGateway" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:eventBasedGateway id="Tor" />
            <bpmn:intermediateCatchEvent id="Freigabe">
              <bpmn:messageEventDefinition id="Definition_Freigabe" messageRef="Message_Freigabe" />
            </bpmn:intermediateCatchEvent>
            <bpmn:intermediateCatchEvent id="Frist">
              <bpmn:timerEventDefinition id="Definition_Frist">
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT1H</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:serviceTask id="NachFreigabe"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nachFreigabe" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="NachFrist"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nachFrist" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="EndeFreigabe" />
            <bpmn:endEvent id="EndeFrist" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Tor" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Tor" targetRef="Freigabe" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="Tor" targetRef="Frist" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="Freigabe" targetRef="NachFreigabe" />
            <bpmn:sequenceFlow id="Flow_5" sourceRef="Frist" targetRef="NachFrist" />
            <bpmn:sequenceFlow id="Flow_6" sourceRef="NachFreigabe" targetRef="EndeFreigabe" />
            <bpmn:sequenceFlow id="Flow_7" sourceRef="NachFrist" targetRef="EndeFrist" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Ein Abbruch von aussen unterbricht die Bearbeitung und uebernimmt.</summary>
    private static string EventSubProcessXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_EventSubProcess" targetNamespace="test">
          <bpmn:message id="Message_Abbruch" name="Abbruch" />
          <bpmn:process id="Process_EventSubProcess" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:serviceTask id="Bearbeiten"><bpmn:extensionElements>
              <zeebe:taskDefinition type="bearbeiten" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Bearbeiten" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Bearbeiten" targetRef="Ende" />
            <bpmn:subProcess id="EventSub" triggeredByEvent="true">
              <bpmn:startEvent id="AbbruchStart">
                <bpmn:messageEventDefinition id="Definition_Abbruch" messageRef="Message_Abbruch" />
              </bpmn:startEvent>
              <bpmn:serviceTask id="Abbrechen"><bpmn:extensionElements>
                <zeebe:taskDefinition type="abbrechen" />
              </bpmn:extensionElements></bpmn:serviceTask>
              <bpmn:endEvent id="EventSubEnde" />
              <bpmn:sequenceFlow id="SubFlow_1" sourceRef="AbbruchStart" targetRef="Abbrechen" />
              <bpmn:sequenceFlow id="SubFlow_2" sourceRef="Abbrechen" targetRef="EventSubEnde" />
            </bpmn:subProcess>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Die Entscheidung liefert die Variablen, auf die der Split seine Zweige stuetzt.</summary>
    private static string InclusiveGatewayXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            id="Definitions_Inclusive" targetNamespace="test">
          <bpmn:process id="Process_Inclusive" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:serviceTask id="Entscheiden"><bpmn:extensionElements>
              <zeebe:taskDefinition type="entscheiden" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:inclusiveGateway id="Split" default="Flow_Standard" />
            <bpmn:serviceTask id="WegA"><bpmn:extensionElements>
              <zeebe:taskDefinition type="wegA" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="WegB"><bpmn:extensionElements>
              <zeebe:taskDefinition type="wegB" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="Standard"><bpmn:extensionElements>
              <zeebe:taskDefinition type="standard" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:inclusiveGateway id="Join" />
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Entscheiden" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Entscheiden" targetRef="Split" />
            <bpmn:sequenceFlow id="Flow_A" sourceRef="Split" targetRef="WegA">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=wegA</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_B" sourceRef="Split" targetRef="WegB">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=wegB</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Standard" sourceRef="Split" targetRef="Standard" />
            <bpmn:sequenceFlow id="Flow_AJoin" sourceRef="WegA" targetRef="Join" />
            <bpmn:sequenceFlow id="Flow_BJoin" sourceRef="WegB" targetRef="Join" />
            <bpmn:sequenceFlow id="Flow_StandardJoin" sourceRef="Standard" targetRef="Join" />
            <bpmn:sequenceFlow id="Flow_Ende" sourceRef="Join" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
