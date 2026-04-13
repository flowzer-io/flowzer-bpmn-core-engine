using System.Text;
using FluentAssertions;
using Model;

namespace core_engine_tests;

public class CoreEngineTest
{
    // Testzweck: Deckt den Fall „Load BPMN File Should Expose Initial Start Subscription“ ab.
    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldExposeInitialStartSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

        var subscriptions = await coreEngine.GetInitialSubscriptions();

        subscriptions.Should().ContainSingle();
        subscriptions[0].Type.Should().Be(CoreSubscriptionType.Start);
        subscriptions[0].BpmnNodeId.Should().Be("StartEvent_1");
    }

    // Testzweck: Deckt den Fall „Load BPMN File Should Expose Message And Signal Subscriptions“ ab.
    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldExposeMessageAndSignalSubscriptions()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

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

    // Testzweck: Deckt den Fall „Load BPMN File Should Hide Unsupported Plain Start Subscriptions When Multiple Plain Starts Exist“ ab.
    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldHideUnsupportedPlainStartSubscriptions_WhenMultiplePlainStartsExist()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMultiPlainStartProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

        var subscriptions = await coreEngine.GetInitialSubscriptions();

        subscriptions.Should().BeEmpty();
    }

    // Testzweck: Deckt den Fall „Handle Event Should Start Process And Expose User Task“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessAndExposeUserTask()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_1"
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].InteractionId.Should().NotBeEmpty();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("UserTask_1");
    }

    // Testzweck: Deckt den Fall „Handle Event Should Start Process From Message Subscription“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessFromMessageSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_Message",
            AdditionalData = new Dictionary<string, object?> { ["orderId"] = "4711" }
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].InteractionId.Should().NotBeEmpty();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("MessageUserTask_1");
    }

    // Testzweck: Deckt den Fall „Handle Event Should Start Process From Signal Subscription“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldStartProcessFromSignalSubscription()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateMessageAndSignalProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

        var result = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = Guid.NewGuid(),
            BpmnNodeId = "StartEvent_Signal",
            AdditionalData = new Dictionary<string, object?> { ["warehouse"] = "north" }
        });

        result.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        result.Instance.Interactions.Should().ContainSingle();
        result.Instance.Interactions[0].InteractionId.Should().NotBeEmpty();
        result.Instance.Interactions[0].Type.Should().Be(CoreInteractionType.UserTask);
        result.Instance.Interactions[0].NodeId.Should().Be("SignalUserTask_1");
    }

    // Testzweck: Deckt den Fall „Handle Event Should Advance User Task To Service Task And Then Finish Process“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldAdvanceUserTaskToServiceTask_AndThenFinishProcess()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());
        var finishedSnapshots = new List<CoreInstance>();
        coreEngine.InteractionFinished += (_, snapshot) => finishedSnapshots.Add(snapshot);

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

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
            InteractionId = startResult.Instance.Interactions[0].InteractionId,
            AdditionalData = new Dictionary<string, object?> { ["approval"] = "approved" }
        });

        var serviceTaskResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "ServiceTask_1",
            InteractionId = userTaskResult.Instance.Interactions[0].InteractionId
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

    // Testzweck: Deckt den Fall „Handle Event Should Allow Restart After Completed Instance Was Cleaned Up“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldAllowRestartAfterCompletedInstanceWasCleanedUp()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(bpmnStream);

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
            InteractionId = startResult.Instance.Interactions[0].InteractionId,
            AdditionalData = new Dictionary<string, object?> { ["approval"] = "approved" }
        });

        var completedResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "ServiceTask_1",
            InteractionId = userTaskResult.Instance.Interactions[0].InteractionId
        });

        completedResult.Instance.State.Should().Be(ProcessInstanceState.Completed);

        var restartedResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "StartEvent_1"
        });

        restartedResult.Instance.State.Should().Be(ProcessInstanceState.Waiting);
        restartedResult.Instance.Interactions.Should().ContainSingle();
        restartedResult.Instance.Interactions[0].NodeId.Should().Be("UserTask_1");
    }

    // Testzweck: Deckt den Fall „Handle Event Should Require Interaction ID When Multiple Active Interactions Share The Same Node“ ab.
    [Test]
    public async System.Threading.Tasks.Task HandleEvent_ShouldRequireInteractionId_WhenMultipleActiveInteractionsShareTheSameNode()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var bpmnStream = File.OpenRead("embeddings/ParallelFlowWithCompletingConditionTest.bpmn");
        await coreEngine.LoadBpmnFile(bpmnStream);

        var instanceId = Guid.NewGuid();
        var startResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "StartEvent_1"
        });

        startResult.Instance.Interactions.Should().HaveCountGreaterThan(1);
        startResult.Instance.Interactions.Select(interaction => interaction.NodeId).Should().OnlyContain(nodeId => nodeId == "TestTask");

        var ambiguousAction = async () => await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = "TestTask",
            AdditionalData = new Dictionary<string, object?> { ["HatZeit"] = true }
        });

        await ambiguousAction.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage("*InteractionId*");

        var continueResult = await coreEngine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = startResult.Instance.Interactions[0].NodeId,
            InteractionId = startResult.Instance.Interactions[0].InteractionId,
            AdditionalData = new Dictionary<string, object?> { ["HatZeit"] = true }
        });

        continueResult.Instance.State.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Deckt den Fall „Load BPMN File Should Clear State When Reload Fails“ ab.
    [Test]
    public async System.Threading.Tasks.Task LoadBpmnFile_ShouldClearState_WhenReloadFails()
    {
        var coreEngine = new CoreEngine(FlowzerConfig.CreateForTests());

        await using var validStream = CreateSimpleProcessStream();
        await coreEngine.LoadBpmnFile(validStream);

        (await coreEngine.GetInitialSubscriptions()).Should().ContainSingle();

        await using var invalidStream = CreateInvalidProcessStream();
        var loadAction = async () => await coreEngine.LoadBpmnFile(invalidStream);

        await loadAction.Should().ThrowAsync<Exception>();

        var subscriptionsAction = async () => await coreEngine.GetInitialSubscriptions();
        await subscriptionsAction.Should().ThrowAsync<InvalidOperationException>();
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

    private static MemoryStream CreateMultiPlainStartProcessStream()
    {
        const string bpmnXml = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                                 xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                                 xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                                 xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                                 id="Definitions_MultiPlainStart"
                                                 targetNamespace="http://bpmn.io/schema/bpmn">
                                 <bpmn:process id="MultiPlainStartProcess" isExecutable="true">
                                   <bpmn:startEvent id="StartEvent_A" name="Start A">
                                     <bpmn:outgoing>Flow_A</bpmn:outgoing>
                                   </bpmn:startEvent>
                                   <bpmn:startEvent id="StartEvent_B" name="Start B">
                                     <bpmn:outgoing>Flow_B</bpmn:outgoing>
                                   </bpmn:startEvent>
                                   <bpmn:userTask id="UserTask_A" name="Task A">
                                     <bpmn:extensionElements>
                                       <zeebe:formDefinition formKey="form-a" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_A</bpmn:incoming>
                                   </bpmn:userTask>
                                   <bpmn:userTask id="UserTask_B" name="Task B">
                                     <bpmn:extensionElements>
                                       <zeebe:formDefinition formKey="form-b" />
                                     </bpmn:extensionElements>
                                     <bpmn:incoming>Flow_B</bpmn:incoming>
                                   </bpmn:userTask>
                                   <bpmn:sequenceFlow id="Flow_A" sourceRef="StartEvent_A" targetRef="UserTask_A" />
                                   <bpmn:sequenceFlow id="Flow_B" sourceRef="StartEvent_B" targetRef="UserTask_B" />
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

    private static MemoryStream CreateInvalidProcessStream()
    {
        const string bpmnXml = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                                 xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                                 xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                                 id="Definitions_Invalid"
                                                 targetNamespace="http://bpmn.io/schema/bpmn">
                                 <bpmn:process id="InvalidProcess" isExecutable="true">
                                   <bpmn:startEvent id="StartEvent_Invalid">
                                     <bpmn:outgoing>Flow_Invalid_1</bpmn:outgoing>
                                   </bpmn:startEvent>
                                   <bpmn:userTask id="UserTask_Invalid" name="Broken User Task">
                                     <bpmn:incoming>Flow_Invalid_1</bpmn:incoming>
                                   </bpmn:userTask>
                                   <bpmn:sequenceFlow id="Flow_Invalid_1" sourceRef="StartEvent_Invalid" targetRef="UserTask_Invalid" />
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        return new MemoryStream(Encoding.UTF8.GetBytes(bpmnXml));
    }
}
