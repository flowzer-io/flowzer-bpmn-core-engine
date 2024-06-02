using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer.Events;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using BPMN.Process;
using core_engine;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class ModelParserTest
{
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
            AssertFlowNodeOfTypes<EndEvent>(process, 1);
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
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalThrowEvent>(process, 1);
            
            AssertFlowNodeOfTypes<SequenceFlow>(process, 18);
        });

        var secondModel = await ModelParser.ParseModel(File.Open("embeddings/AllFlowNodes.bpmn", FileMode.Open));
        Assert.That(process.GetHashCode(), Is.EqualTo(secondModel.GetProcesses().Single().GetHashCode()),
            "Der Hashcode des Processes muss bei mehrmaligem Aufruf gleich bleiben");
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