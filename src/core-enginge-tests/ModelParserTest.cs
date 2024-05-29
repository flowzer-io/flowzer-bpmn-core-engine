using System.Dynamic;
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

        var variables = new ExpandoObject();
        variables.TryAdd("ServiceResult", "World123");
        
        instanceEngine.HandleServiceTaskResult(serviceTaskToken.Id,variables );
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));

        IDictionary<string,object> instanceProcessVariables = instanceEngine.Instance.ProcessVariables;
        Assert.That(instanceProcessVariables["GlobalResult"], Is.EqualTo("World123"));
        
    }
}