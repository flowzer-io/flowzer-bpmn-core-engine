using core_engine;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class ModelParserTest
{
    [Test]
    public async Task ReadProcessesTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/StartStopWithVariables.bpmn",FileMode.Open));
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));
        
    }
}