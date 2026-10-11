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

    // Testzweck: Prüft den Signalfluss mit Start- und Intermediate-Signal-Ereignis innerhalb derselben Instanz.
    [Test]
    public void Flow1Test()
    {
        var instanceEngines = Helper.CreateProcessEngine(Process).HandleSignal("SignalStartOne");
        AssertCanonicalScope(instanceEngines.Single());
        using (new AssertionScope())
        {
            instanceEngines.Should().ContainSingle();
            instanceEngines[0].ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        }
        
        instanceEngines[0].HandleSignal("SignalIntermediate");
        AssertCanonicalScope(instanceEngines[0]);
        using (new AssertionScope())
        {
            instanceEngines[0].ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngines[0].Tokens.Should().HaveCount(4);
        }
    }

    // Testzweck: Prüft, dass ein Signalstart mehrere Instanzen mit unterschiedlichen Pfaden auslösen kann.
    [Test]
    public void Flow2Test()
    {
        var instanceEngines = Helper.CreateProcessEngine(Process).HandleSignal("SignalStartTwo");
        foreach (var instance in instanceEngines) AssertCanonicalScope(instance);
        var engine = instanceEngines.SingleOrDefault(engine => engine.ProcessInstanceState == ProcessInstanceState.Waiting);
        using (new AssertionScope())
        {
            instanceEngines.Should().HaveCount(2);
            engine.Should().NotBeNull();
            instanceEngines.Where(e => e.ProcessInstanceState == ProcessInstanceState.Completed)
                .Should().ContainSingle();
            
            engine?.HandleServiceTaskResult("step1");
            if (engine is not null) AssertCanonicalScope(engine);
            engine?.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        }
    }

    // Testzweck: Mehrere echte Signal-Starts erzeugen getrennte Instanzen, aber jeder
    // Start- und Folgetoken bleibt im kanonischen Scope seines Masters, auch nach dem Warten.
    [Test]
    public void SignalStart_ShouldKeepCanonicalScopesWhileWaitingAndCompleting()
    {
        var instances = Helper.CreateProcessEngine(Process).HandleSignal("SignalStartTwo");
        instances.Should().HaveCount(2);
        instances.Select(instance => instance.MasterToken.ProcessInstanceId).Should().OnlyHaveUniqueItems();
        foreach (var instance in instances) AssertCanonicalScope(instance);
        var waiting = instances.Single(instance => instance.ProcessInstanceState == ProcessInstanceState.Waiting);
        waiting.HandleServiceTaskResult("step1");
        waiting.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        foreach (var instance in instances) AssertCanonicalScope(instance);
    }

    private static void AssertCanonicalScope(InstanceEngine instance)
    {
        var scope = instance.MasterToken.ProcessInstanceId;
        scope.Should().NotBeEmpty().And.NotBe(instance.MasterToken.Id).And.NotBe(instance.InstanceId);
        instance.Tokens.Should().OnlyContain(token => token.ProcessInstanceId == scope);
    }
}
