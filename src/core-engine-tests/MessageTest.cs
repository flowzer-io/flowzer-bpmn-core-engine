using BPMN.Infrastructure;
using BPMN.Process;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;

namespace core_engine_tests;

public class MessageTest
{
    private static Definitions Definitions { get; } = ModelParser.ParseModel(File.OpenRead("embeddings/Messages.bpmn")).GetAwaiter().GetResult();
    private Process Process { get; } = Definitions.GetProcesses().First();
    
    [Test]
    public void Flow1Test()
    {
        var testMessage = new Message { Name = string.Empty, TimeToLive = 60, CorrelationKey = "12345" };
        var instanceEngine = new ProcessEngine(Process).StartProcess();
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.MasterToken.Variables.GetValue("AuftragsNr").Should().Be(12345); 
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.GetActiveCatchMessages().Should().HaveCount(2);
            instanceEngine.GetActiveCatchMessages().Select(i => i.Name)
                .Should().Contain(["NachrichtBoundary", "NachrichtBoundaryNI"]);
            instanceEngine.GetActiveCatchMessages().First().FlowzerCorrelationKey.Should().Be("12345");
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtBoundaryNI" });
        
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(3);
            instanceEngine.GetActiveCatchMessages().Should().HaveCount(2);
            instanceEngine.HandleServiceTaskResult("step2");
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.GetActiveCatchMessages().Should().HaveCount(2);
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtBoundary"});
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.GetActiveCatchMessages().Should().BeEmpty();
        }
        
        instanceEngine = new ProcessEngine(Process).StartProcess();
        instanceEngine.HandleServiceTaskResult("step1");
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.Tokens.Should().HaveCount(4);
            instanceEngine.GetActiveCatchMessages().Should().ContainSingle();
            instanceEngine.GetActiveCatchMessages().Single().Name.Should().Be("NachrichtReceive");
            instanceEngine.GetActiveCatchMessages().Single().FlowzerCorrelationKey.Should().Be("12345");
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtReceive" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.GetActiveCatchMessages().Should().ContainSingle();
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtIntermediate" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.GetActiveCatchMessages().Should().BeEmpty();
        }
    }

    [Test]
    public void Flow2Test()
    {
        var instanceEngine =
            new ProcessEngine(Process).HandleMessage(new Message { Name = "NachrichtStart" });
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.Tokens.Should().HaveCount(3);
        }
    }
}