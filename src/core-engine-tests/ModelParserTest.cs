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
            AssertFlowNodeOfTypes<ScriptTask>(process, 1);
            AssertFlowNodeOfTypes<ServiceTask>(process, 1);
            AssertFlowNodeOfTypes<StartEvent>(process, 1);
            AssertFlowNodeOfTypes<EndEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerBoundaryMessageEvent>(process, 1);
            AssertFlowNodeOfTypes<IntermediateThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<UserTask>(process, 1);
            AssertFlowNodeOfTypes<ParallelGateway>(process, 2);
            AssertFlowNodeOfTypes<ManualTask>(process, 1);
        });
    }

    private static void AssertFlowNodeOfTypes<T>(Process process, int count) where T : FlowNode
    {
        Assert.That(process.FlowElements.OfType<T>().Count(), Is.EqualTo(count), $"{typeof(T).Name} Count");
    }
}