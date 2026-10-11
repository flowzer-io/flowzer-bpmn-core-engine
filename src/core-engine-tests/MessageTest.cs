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
        AssertCanonicalScope(instanceEngine);
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
        AssertCanonicalScope(instanceEngine);
        
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(3);
            instanceEngine.ActiveCatchMessages.Should().HaveCount(2);
            instanceEngine.HandleServiceTaskResult("step2");
            AssertCanonicalScope(instanceEngine);
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Should().HaveCount(2);
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtBoundary"});
        AssertCanonicalScope(instanceEngine);
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
        
        instanceEngine = Helper.CreateProcessEngine(Process).StartProcess();
        AssertCanonicalScope(instanceEngine);
        instanceEngine.HandleServiceTaskResult("step1");
        AssertCanonicalScope(instanceEngine);
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.Tokens.Should().HaveCount(4);
            instanceEngine.ActiveCatchMessages.Should().ContainSingle();
            instanceEngine.ActiveCatchMessages.Single().Name.Should().Be("NachrichtReceive");
            instanceEngine.ActiveCatchMessages.Single().FlowzerCorrelationKey.Should().Be("12345");
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtReceive" });
        AssertCanonicalScope(instanceEngine);
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveTokens.Should().HaveCount(2);
            instanceEngine.ActiveCatchMessages.Should().ContainSingle();
        }
        
        instanceEngine.HandleMessage(testMessage with { Name = "NachrichtIntermediate" });
        AssertCanonicalScope(instanceEngine);
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

    // Testzweck: Ein echter Message-Start muss für Start- und Folgetokens denselben
    // internen Master-Scope verwenden, nicht die getrennte Master-Token-Identität.
    [Test]
    public void MessageStart_ShouldKeepCanonicalScopeAcrossOutgoingTokens()
    {
        var instance = Helper.CreateProcessEngine(Process).HandleMessage(new Message { Name = "NachrichtStart" });
        instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        instance.Tokens.Should().HaveCount(3);
        AssertCanonicalScope(instance);
    }

    private static void AssertCanonicalScope(InstanceEngine instance)
    {
        var scope = instance.MasterToken.ProcessInstanceId;
        scope.Should().NotBeEmpty().And.NotBe(instance.MasterToken.Id).And.NotBe(instance.InstanceId);
        instance.Tokens.Should().OnlyContain(token => token.ProcessInstanceId == scope);
    }
}
