using BPMN.Activities;
using BPMN.Events;
using core_engine;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class EngineTest
{
    [Test]
    public async Task StartStopWithVariables()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("StartStopWithVariables.bpmn");
        
        //should wait for service task
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        
        //should have one active service task
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();
        var serviceTask = serviceTaskToken.CurrentFlowNode as ServiceTask;
        Assert.That(serviceTask?.Id, Is.EqualTo("ServiceTask_1"));
        
        //inject service result
        var variables = new
        {
            ServiceResult= "World123"
        };
        instanceEngine.HandleServiceTaskResult(serviceTaskToken.Id,variables.ToDynamic());
        
        //should be completed
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));

        //check if global variable is set
        Assert.That(((dynamic)instanceEngine.Instance.ProcessVariables).GlobalResult, Is.EqualTo("World123"));
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.Instance.Tokens, Has.Count.EqualTo(3));
        });

    }

    [Test]
    public async Task VariableMappingTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("VariablesTest.bpmn");
        
        //should have one active service task
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();
 
        Assert.That(((dynamic)serviceTaskToken.InputData).Firstname, Is.EqualTo("Lukas"));
        
        var variables = new
        {
            Out= new
            {
                Firstname = "LukasNeu"
            } 
        };
        instanceEngine.HandleServiceTaskResult(serviceTaskToken.Id,variables.ToDynamic());

        //check if global variable is set
        Assert.That((string?) instanceEngine.Instance.ProcessVariables.GetValue("Order.Address.NewFirstname"), Is.EqualTo("LukasNeu"));
    }


    [Test]
    public async Task ConditionalSequenceFlowTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ConditionalSequenceFlow.bpmn");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.Instance.Tokens, Has.Count.EqualTo(3));
            Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldReached"), Is.EqualTo(1));
            Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldNotReached"), Is.EqualTo(0));
        });
    }
    
    [Test]
    public async Task ParallelGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoin.bpmn");
        Helper.DoTestServiceThings(instanceEngine);
        
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        
        var endEventTokens = instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).ToList();
        
        Assert.That(endEventTokens.Count, Is.EqualTo(1));
        
    }
    
        
    [Test]
    public async Task ParallelGatewayComplexTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoinComplex.bpmn");
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));

        var tokensAtParallelGateway = instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        Assert.That(tokensAtParallelGateway.Length, Is.EqualTo(1));
        Assert.That(instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).Count, Is.EqualTo(0));
        
        
        //do step1
        instanceEngine.HandleServiceTaskResult(instanceEngine.GetActiveServiceTasks().Single(x=>x.CurrentFlowNode is ServiceTask st && st.Implementation== "step1").Id, new object());
        tokensAtParallelGateway = instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        Assert.That(tokensAtParallelGateway.Length, Is.EqualTo(2));
        Assert.That(tokensAtParallelGateway.All(x=>x.LastSequenceFlow?.Id == tokensAtParallelGateway.First().LastSequenceFlow?.Id));
        Assert.That(instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).Count, Is.EqualTo(0));

        
        //do step2
        instanceEngine.HandleServiceTaskResult(instanceEngine.GetActiveServiceTasks().Single(x=>x.CurrentFlowNode is ServiceTask st && st.Implementation== "step2").Id, new object());
        tokensAtParallelGateway = instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        Assert.That(tokensAtParallelGateway.Length, Is.EqualTo(1));
        Assert.That(instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).Count, Is.EqualTo(1));
        
        
        //do step3
        instanceEngine.HandleServiceTaskResult(instanceEngine.GetActiveServiceTasks().Single(x=>x.CurrentFlowNode is ServiceTask st && st.Implementation== "step3").Id, new object());
        Assert.That(instanceEngine.ActiveTokens.Count(), Is.EqualTo(0));
        Assert.That(instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).Count, Is.EqualTo(2));
        
        
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));

    }
        
    [Test]
    public async Task ExklusiveGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ExklusiveGateway.bpmn");
        
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldReached"), Is.EqualTo(1));
        Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldNotReached"), Is.EqualTo(0));
    }

    

}