using BPMN.Infrastructure;
using BPMN.Process;
using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;

namespace core_engine_tests;

public class MessageTest
{
    private static Definitions Definitions { get; } = ModelParser.ParseModel(File.OpenRead("embeddings/Messages.bpmn")).GetAwaiter().GetResult();
    private Process Process { get; } = Definitions.GetProcesses().First();
    
    // Testzweck: Prüft den vollständigen Nachrichtenfluss mit Boundary-, Receive- und Intermediate-Message-Ereignissen.
    [Test]
    public void Flow1Test()
    {
        var testMessage = new Message { Name = string.Empty, TimeToLive = 60, CorrelationKey = "12345" };
        var instanceEngine = Helper.CreateProcessEngine(Process).StartProcess();
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.MasterToken.Variables.GetValue("AuftragsNr").Should().Be(12345); 
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Select(i => i.Name)
                .Should().Contain(["NachrichtBoundary", "NachrichtBoundaryNI"]);
            instanceEngine.ActiveCatchMessages.First().FlowzerCorrelationKey.Should().Be("12345");
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtBoundaryNI" });
        
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(3);
            instanceEngine.ActiveCatchMessages.Should().HaveCount(2);
            instanceEngine.HandleServiceTaskResult("step2");
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Should().HaveCount(2);
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtBoundary"});
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
        
        instanceEngine = Helper.CreateProcessEngine(Process).StartProcess();
        instanceEngine.HandleServiceTaskResult("step1");
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.Tokens.Should().HaveCount(4);
            instanceEngine.ActiveCatchMessages.Should().ContainSingle();
            instanceEngine.ActiveCatchMessages.Single().Name.Should().Be("NachrichtReceive");
            instanceEngine.ActiveCatchMessages.Single().FlowzerCorrelationKey.Should().Be("12345");
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtReceive" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Should().ContainSingle();
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtIntermediate" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
    }

    // Testzweck: Prüft, dass ein Message-Start-Event direkt eine neue Instanz bis zum Abschluss auslösen kann.
    [Test]
    public void Flow2Test()
    {
        var instanceEngine =
            Helper.CreateProcessEngine(Process).HandleMessage(new Message { Name = "NachrichtStart" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.Tokens.Should().HaveCount(3);
        }
    }
}
