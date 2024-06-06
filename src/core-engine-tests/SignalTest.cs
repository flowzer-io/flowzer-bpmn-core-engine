using BPMN.Infrastructure;
using BPMN.Process;
using Model;

namespace core_engine_tests;

public class SignalTest
{
    private static Definitions Definitions { get; } =
        ModelParser.ParseModel(File.OpenRead("embeddings/Signals.bpmn")).GetAwaiter().GetResult();

    private Process Process { get; } = Definitions.GetProcesses().First();

    [Test]
    public void Flow1Test()
    {
        var instanceEngines = new ProcessEngine(Process).HandleSignal("SignalStartOne");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngines, Has.Length.EqualTo(1));
            Assert.That(instanceEngines[0].Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        });
        
        instanceEngines[0].HandleSignal("SignalIntermediate");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngines[0].Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngines[0].Instance.Tokens, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Flow2Test()
    {
        var instanceEngines = new ProcessEngine(Process).HandleSignal("SignalStartTwo");
        var engine = instanceEngines.SingleOrDefault(engine => engine.Instance.State == ProcessInstanceState.Waiting);
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngines, Has.Length.EqualTo(2));
            Assert.That(engine, Is.Not.Null);
            Assert.That(instanceEngines.Count(e => e.Instance.State == ProcessInstanceState.Completed),
                Is.EqualTo(1));
            
            engine?.HandleServiceTaskResult("step1");
            Assert.That(engine?.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        });
    }
}