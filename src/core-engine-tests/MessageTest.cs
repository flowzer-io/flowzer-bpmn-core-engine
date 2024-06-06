using BPMN.Infrastructure;
using BPMN.Process;
using Model;

namespace core_engine_tests;

public class MessageTest
{
    private static Definitions Definitions { get; } = ModelParser.ParseModel(File.OpenRead("embeddings/Messages.bpmn")).GetAwaiter().GetResult();
    private Process Process { get; } = Definitions.GetProcesses().First();
    
    [Test]
    public void Flow1Test()
    {
        var instanceEngine = new ProcessEngine(Process).StartProcess();
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That((int?)instanceEngine.Instance.ProcessVariables.GetValue("AuftragsNr"),
                Is.EqualTo(12345)); 
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(1));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(2));
            Assert.That(instanceEngine.GetActiveCatchMessages().Select(i => i.Name), Contains.Item("NachrichtBoundary"));
            Assert.That(instanceEngine.GetActiveCatchMessages().Select(i => i.Name), Contains.Item("NachrichtBoundaryNI"));
            Assert.That(instanceEngine.GetActiveCatchMessages().First().FlowzerCorrelationKey, Is.EqualTo("12345"));
        });
        
        instanceEngine.HandleMessage(new Message {Name = "NachrichtBoundaryNI", CorrelationKey = "12345", TimeToLive = 60});
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(2));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(2));
            instanceEngine.HandleServiceTaskResult("step2");
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(1));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(2));
        });
        
        instanceEngine.HandleMessage(new Message {Name = "NachrichtBoundary", CorrelationKey = "12345", TimeToLive = 60});
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(0));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(0));
        });
        
        instanceEngine = new ProcessEngine(Process).StartProcess();
        instanceEngine.HandleServiceTaskResult("step1");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That(instanceEngine.Instance.Tokens, Has.Count.EqualTo(3));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(1));
            Assert.That(instanceEngine.GetActiveCatchMessages().Single().Name, Is.EqualTo("NachrichtReceive"));
            Assert.That(instanceEngine.GetActiveCatchMessages().Single().FlowzerCorrelationKey, Is.EqualTo("12345"));
        });
        
        instanceEngine.HandleMessage(new Message {Name = "NachrichtReceive", CorrelationKey = "12345", TimeToLive = 60});
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(1));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(1));
        });
        
        instanceEngine.HandleMessage(new Message {Name = "NachrichtIntermediate", CorrelationKey = "12345", TimeToLive = 60});
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(0));
            Assert.That(instanceEngine.GetActiveCatchMessages(), Has.Count.EqualTo(0));
        });
    }

    [Test]
    public void Flow2Test()
    {
        var instanceEngine =
            new ProcessEngine(Process).HandleMessage(new Message() { Name = "NachrichtStart", TimeToLive = 60 });
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(0));
            Assert.That(instanceEngine.Instance.Tokens, Has.Count.EqualTo(2));
        });
    }
}