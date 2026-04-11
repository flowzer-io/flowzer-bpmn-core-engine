using System.Text;
using FluentAssertions;
using Model;

namespace core_engine_tests;

public class CoreEngineTest
{
    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldExposeInitialStartSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var subscriptions = await coreEngine.GetInitialSubscriptions();

        subscriptions.Should().ContainSingle();
        subscriptions[0].Type.Should().Be(CoreSubscriptionType.Start);
        subscriptions[0].BpmnNodeId.Should().Be("StartEvent_1");
    }

    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldExposeMessageAndSignalSubscriptions()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var subscriptions = await coreEngine.GetInitialSubscriptions();

        subscriptions.Should().HaveCount(2);
        subscriptions.Should().ContainSingle(subscription =>
            subscription.Type == CoreSubscriptionType.Message &&
            subscription.BpmnNodeId == "StartEvent_Message" &&
            subscription.Name == "OrderCreated");
        subscriptions.Should().ContainSingle(subscription =>
            subscription.Type == CoreSubscriptionType.Signal &&
            subscription.BpmnNodeId == "StartEvent_Signal" &&
            subscription.Name == "InventoryRefresh");
    }

    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessAndExposeUserTask()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_1"
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("UserTask_1");
    }

    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessFromMessageSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_Message",
            AdditionalData = new Dictionary<string, object?> { ["orderId"] = "4711" }
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("MessageUserTask_1");
    }

    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessFromSignalSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_Signal",
            AdditionalData = new Dictionary<string, object?> { ["warehouse"] = "north" }
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("SignalUserTask_1");
    }

    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldAdvanceUserTaskToServiceTask_AndThenFinishProcess()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());
        var finishedSnapshots = new List<CoreInstance>();
        coreEngine.InteractionFinished += (_, snapshot) => finishedSnapshots.Add(snapshot);

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream, verify: true);

        var instanceId = Guid.NewGuid();

        var startResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "StartEvent_1"
        });

        var userTaskResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "UserTask_1",
            AdditionalData = new Dictionary<string, object?> { ["approval"] = "approved" }
        });

        var serviceTaskResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "ServiceTask_1"
        });

        startResult.Instance.Interactions.Should().ContainSingle();
        userTaskResult.Instance.Interactions.Should().ContainSingle();
        userTaskResult.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.ServiceTask);
        userTaskResult.Instance.Interactions[0].NodeId.Should().Be("ServiceTask_1");

        serviceTaskResult.Instance.State.Should().Be(ProcessInstanceState.Completed);
        serviceTaskResult.Instance.Interactions.Should().BeEmpty();
        finishedSnapshots.Should().HaveCount(3);
        finishedSnapshots[^1].State.Should().Be(ProcessInstanceState.Completed);
    }

    private static MemoryStream CreateSimpleProcessStream()
    {
        const string bpmnXml = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                                 xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                                 xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                                 xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                                 id="Definitions_1"
                                                 targetNamespace="http://bpmn.io/schema/bpmn">
                                 <bpmn:process id="SimpleProcess" isExecutable="true">
                                   <bpmn:startEvent id="StartEvent_1" name="Start">
                                     <bpmn:outgoing>Flow_1</bpmn:outgoing>
                                   </bpmn:startEvent>
                                   <bpmn:userTask id="UserTask_1" name="Review">
                                     <bpmn:extensionElements>
                                       <zeebe:formDefinition formKey="core-engine-test-form" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_1</bpmn:incoming>
                                     <bpmn:outgoing>Flow_2</bpmn:outgoing>
                                   </bpmn:userTask>
                                   <bpmn:serviceTask id="ServiceTask_1" name="Notify">
                                     <bpmn:extensionElements>
                                       <zeebe:taskDefinition type="notify-handler" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_2</bpmn:incoming>
                                     <bpmn:outgoing>Flow_3</bpmn:outgoing>
                                   </bpmn:serviceTask>
                                   <bpmn:endEvent id="EndEvent_1" name="Done">
                                     <bpmn:incoming>Flow_3</bpmn:incoming>
                                   </bpmn:endEvent>
                                   <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />
                                   <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="ServiceTask_1" />
                                   <bpmn:sequenceFlow id="Flow_3" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        return new MemoryStream(Encoding.UTF8.GetBytes(bpmnXml));
    }

    private static MemoryStream CreateMessageAndSignalProcessStream()
    {
        const string bpmnXml = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                                 xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                                 xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                                 xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                                 id="Definitions_MessageAndSignal"
                                                 targetNamespace="http://bpmn.io/schema/bpmn">
                                 <bpmn:message id="Message_1" name="OrderCreated" />
                                 <bpmn:signal id="Signal_1" name="InventoryRefresh" />
                                 <bpmn:process id="EventStartProcess" isExecutable="true">
                                   <bpmn:startEvent id="StartEvent_Message" name="Message Start">
                                     <bpmn:outgoing>Flow_Message_1</bpmn:outgoing>
                                     <bpmn:messageEventDefinition id="MessageEventDefinition_1" messageRef="Message_1" />
                                   </bpmn:startEvent>
                                   <bpmn:userTask id="MessageUserTask_1" name="Handle Message">
                                     <bpmn:extensionElements>
                                       <zeebe:formDefinition formKey="message-start-form" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_Message_1</bpmn:incoming>
                                     <bpmn:outgoing>Flow_Message_2</bpmn:outgoing>
                                   </bpmn:userTask>
                                   <bpmn:endEvent id="EndEvent_Message">
                                     <bpmn:incoming>Flow_Message_2</bpmn:incoming>
                                   </bpmn:endEvent>
                                   <bpmn:startEvent id="StartEvent_Signal" name="Signal Start">
                                     <bpmn:outgoing>Flow_Signal_1</bpmn:outgoing>
                                     <bpmn:signalEventDefinition id="SignalEventDefinition_1" signalRef="Signal_1" />
                                   </bpmn:startEvent>
                                   <bpmn:userTask id="SignalUserTask_1" name="Handle Signal">
                                     <bpmn:extensionElements>
                                       <zeebe:formDefinition formKey="signal-start-form" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_Signal_1</bpmn:incoming>
                                     <bpmn:outgoing>Flow_Signal_2</bpmn:outgoing>
                                   </bpmn:userTask>
                                   <bpmn:endEvent id="EndEvent_Signal">
                                     <bpmn:incoming>Flow_Signal_2</bpmn:incoming>
                                   </bpmn:endEvent>
                                   <bpmn:sequenceFlow id="Flow_Message_1" sourceRef="StartEvent_Message" targetRef="MessageUserTask_1" />
                                   <bpmn:sequenceFlow id="Flow_Message_2" sourceRef="MessageUserTask_1" targetRef="EndEvent_Message" />
                                   <bpmn:sequenceFlow id="Flow_Signal_1" sourceRef="StartEvent_Signal" targetRef="SignalUserTask_1" />
                                   <bpmn:sequenceFlow id="Flow_Signal_2" sourceRef="SignalUserTask_1" targetRef="EndEvent_Signal" />
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        return new MemoryStream(Encoding.UTF8.GetBytes(bpmnXml));
    }
}
