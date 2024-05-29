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
    public async Task ReadProcessesTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/StartStopWithVariables.bpmn",FileMode.Open));
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));
        
    }
}