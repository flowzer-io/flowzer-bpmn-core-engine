using BPMN.Infrastructure;
using BPMN.Process;
using FluentAssertions;
using FluentAssertions.Execution;
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
        using (new AssertionScope())
        {
            instanceEngines.Should().ContainSingle();
            instanceEngines[0].Instance.State.Should().Be(ProcessInstanceState.Waiting);
        }
        
        instanceEngines[0].HandleSignal("SignalIntermediate");
        using (new AssertionScope())
        {
            instanceEngines[0].Instance.State.Should().Be(ProcessInstanceState.Completed);
            instanceEngines[0].Instance.Tokens.Should().HaveCount(3);
        }
    }

    [Test]
    public void Flow2Test()
    {
        var instanceEngines = new ProcessEngine(Process).HandleSignal("SignalStartTwo");
        var engine = instanceEngines.SingleOrDefault(engine => engine.Instance.State == ProcessInstanceState.Waiting);
        using (new AssertionScope())
        {
            instanceEngines.Should().HaveCount(2);
            engine.Should().NotBeNull();
            instanceEngines.Where(e => e.Instance.State == ProcessInstanceState.Completed)
                .Should().ContainSingle();
            
            engine?.HandleServiceTaskResult("step1");
            engine?.Instance.State.Should().Be(ProcessInstanceState.Completed);
        }
    }
}