using BPMN.Activities;
using core_engine;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace core_enginge_tests;

public class ModelParserTest
{
    [Test]
    public async Task Test1()
    {
        var model = await ModelParser.ParseModel(System.IO.File.Open("embeddings/Test.bpmn",FileMode.Open));
        var process = model.GetProcesses();
        
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));
        
        var defintion = new ProcessDefinition()
        {
            Process = process.First()
        };

        var instance = defintion.StartProcess();
        
        Assert.That(instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        
        var serviceTaskToken = instance.GetActiveServiceTasks().ToArray().First();
        var serviceTask = serviceTaskToken.CurrentFlowNode as ServiceTask;
        Assert.That(serviceTask?.Id, Is.EqualTo("ServiceTask_1"));
        
        
        instance.HandleServiceTaskResult(serviceTaskToken.Id, JObject.FromObject(new {ServiceResult = "Hello World"}));
        Assert.That(instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        
        Assert.That(instance.ProcessVariables?.GetValue("GlobalResult")?.Value<string>(), Is.EqualTo("Hello World"));
        
    }
}