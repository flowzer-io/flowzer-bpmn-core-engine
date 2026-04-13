using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Flowzer.Events;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using FluentAssertions.Execution;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class ModelParserTest
{
    // Testzweck: Prüft, dass der Parser die zentrale Beispiel-BPMN mit allen erwarteten Flow-Node-Typen einliest.
    [Test]
    public async Task ReadProcessesTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/AllFlowNodes.bpmn", FileMode.Open));
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));

        var process = model.GetProcesses().Single();
        Assert.Multiple(() =>
        {
            Assert.That(process.Id, Is.EqualTo("Process_AllNodes"));
            AssertFlowNodeOfTypes<FlowzerScriptTask>(process, 1, "Activity_04cp0f7");
            AssertFlowNodeOfTypes<ServiceTask>(process, 1);
            AssertFlowNodeOfTypes<StartEvent>(process, 1);
            AssertFlowNodeOfTypes<EndEvent>(process, 3);
            AssertFlowNodeOfTypes<FlowzerBoundaryMessageEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerBoundarySignalEvent>(process, 1);
            AssertFlowNodeOfTypes<IntermediateThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<UserTask>(process, 1);
            AssertFlowNodeOfTypes<ParallelGateway>(process, 2);
            AssertFlowNodeOfTypes<ManualTask>(process, 1);
            AssertFlowNodeOfTypes<ExclusiveGateway>(process, 1);
            AssertFlowNodeOfTypes<FlowzerTerminateEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerMessageStartEvent>(process, 1);
            AssertFlowNodeOfTypes<ReceiveTask>(process, 1);
            AssertFlowNodeOfTypes<FlowzerMessageEndEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerSignalStartEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerSignalEndEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerTimerStartEvent>(process, 2);
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateTimerCatchEvent>(process, 1);
            
            AssertFlowNodeOfTypes<SequenceFlow>(process, 21);
        });

        var secondModel = await ModelParser.ParseModel(File.Open("embeddings/AllFlowNodes.bpmn", FileMode.Open));
        Assert.That(process.GetHashCode(), Is.EqualTo(secondModel.GetProcesses().Single().GetHashCode()),
            "Der Hashcode des Processes muss bei mehrmaligem Aufruf gleich bleiben");
    }

    // Testzweck: Prüft, dass Subprozesse, verschachtelte Subprozesse und Call Activities korrekt geparst werden.
    [Test]
    public async Task SubprocessParseTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/SubProcess.bpmn", FileMode.Open));
        model.GetProcesses().Should().ContainSingle();
        var process = model.GetProcesses().Single();
        process.FlowElements.OfType<SubProcess>().Select(p => p.Id)
            .Should().HaveCount(3)
            .And.Contain(["Activity_Sub1", "Activity_Sub2", "Activity_Sub3"]);
        process.FlowElements.OfType<SubProcess>().Single(p => p.Id == "Activity_Sub1").FlowElements
            .OfType<ServiceTask>()
            .Should().ContainSingle(t => t.Name == "Sub1_Step1");
        var subProcess = process.FlowElements.OfType<SubProcess>().Single(p => p.Id == "Activity_Sub2");
        subProcess.FlowElements.OfType<SubProcess>().Should().ContainSingle();
        subProcess.FlowElements.OfType<ServiceTask>().Should().ContainSingle();
        process.FlowElements.OfType<CallActivity>().Should().ContainSingle();
        process.FlowElements.OfType<SubProcess>().Count(p => p.LoopCharacteristics != null).Should().Be(1);
    }

    // Testzweck: Deckt den Fall „Parse Model Should Only Return Executable Processes“ ab.
    [Test]
    public async Task ParseModel_ShouldOnlyReturnExecutableProcesses()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_ExecutableFilter">
                             <bpmn:process id="ExecutableProcess" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                             </bpmn:process>
                             <bpmn:process id="CallableButInactiveProcess" isExecutable="false">
                               <bpmn:startEvent id="StartEvent_2" />
                             </bpmn:process>
                             <bpmn:process id="MissingExecutableFlag">
                               <bpmn:startEvent id="StartEvent_3" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var model = await ModelParser.ParseModel(stream);

        model.GetProcesses()
            .Select(process => process.Id)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("ExecutableProcess");
    }

    // Testzweck: Deckt den Fall „Parse Model Should Parse Boundary Timer Event“ ab.
    [Test]
    public void ParseModel_ShouldParseBoundaryTimerEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_BoundaryTimer"
                                             targetNamespace="http://bpmn.io/schema/bpmn">
                             <bpmn:process id="Process_BoundaryTimer" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:outgoing>Flow_1</bpmn:outgoing>
                               </bpmn:startEvent>
                               <bpmn:serviceTask id="Activity_1" name="Wait for boundary timer">
                                 <bpmn:extensionElements>
                                   <zeebe:taskDefinition type="wait-for-boundary" />
                                 </bpmn:extensionElements>
                                 <bpmn:incoming>Flow_1</bpmn:incoming>
                                 <bpmn:outgoing>Flow_2</bpmn:outgoing>
                               </bpmn:serviceTask>
                               <bpmn:boundaryEvent id="BoundaryTimer_1" cancelActivity="false" attachedToRef="Activity_1">
                                 <bpmn:outgoing>Flow_3</bpmn:outgoing>
                                 <bpmn:timerEventDefinition id="TimerDefinition_1">
                                   <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT5S</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:boundaryEvent>
                               <bpmn:endEvent id="EndEvent_Main">
                                 <bpmn:incoming>Flow_2</bpmn:incoming>
                               </bpmn:endEvent>
                               <bpmn:endEvent id="EndEvent_Boundary">
                                 <bpmn:incoming>Flow_3</bpmn:incoming>
                               </bpmn:endEvent>
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="Activity_1" />
                               <bpmn:sequenceFlow id="Flow_2" sourceRef="Activity_1" targetRef="EndEvent_Main" />
                               <bpmn:sequenceFlow id="Flow_3" sourceRef="BoundaryTimer_1" targetRef="EndEvent_Boundary" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var model = ModelParser.ParseModel(xml);
        var process = model.GetProcesses().Single();
        var boundaryTimer = process.FlowElements.OfType<FlowzerBoundaryTimerEvent>().Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            boundaryTimer.Id.Should().Be("BoundaryTimer_1");
            boundaryTimer.CancelActivity.Should().BeFalse();
            boundaryTimer.AttachedToRef.Id.Should().Be("Activity_1");
            boundaryTimer.TimerType.Should().Be(FlowzerTimerType.TimeDuration);
            boundaryTimer.TimerDefinition.TimeDuration!.Body.Should().Be("PT5S");
        }
    }

    private static void AssertFlowNodeOfTypes<T>(Process process, int? count, string? id = null, string? name = null)
        where T : FlowElement
    {
        if (count is not null)
            Assert.That(process.FlowElements.Count(element => element.GetType() == typeof(T)), Is.EqualTo(count), $"{typeof(T).Name} Count");
        if (id is not null)
            Assert.That(process.FlowElements.OfType<T>().Select(fe => fe.Id), Has.One.AnyOf(id),
                $"{typeof(T).Name}.Id");
        if (name is not null)
            Assert.That(process.FlowElements.OfType<T>().Select(fe => fe.Name), Has.One.AnyOf(name),
                $"{typeof(T).Name}.Name");
        if (count is null && id is null && name is null) Assert.Fail("No assertion specified");
    }
}
