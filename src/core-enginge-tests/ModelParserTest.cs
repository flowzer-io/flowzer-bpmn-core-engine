using BPMN.Activities;
using core_engine;
using Model;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace core_enginge_tests;

public class ModelParserTest
{
    [Test]
    public async Task Test1()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/Test.bpmn",FileMode.Open));
        var process = model.GetProcesses();
        
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));

        var processEngine = new ProcessEngine(process.First());

        var instanceEngine = processEngine.StartProcess();
        
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();
        var serviceTask = serviceTaskToken.CurrentFlowNode as ServiceTask;
        Assert.That(serviceTask?.Id, Is.EqualTo("ServiceTask_1"));
        
        
        instanceEngine.HandleServiceTaskResult(serviceTaskToken.Id, JObject.FromObject(new {ServiceResult = "Hello World"}));
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        
        Assert.That(instanceEngine.Instance.ProcessVariables.GetValue("GlobalResult")?.Value<string>(), Is.EqualTo("Hello World"));
        
    }
}