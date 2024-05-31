using BPMN.Activities;
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
    public async Task ConditionalSequenceFlow()
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
    

}