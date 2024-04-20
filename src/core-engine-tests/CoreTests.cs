using core_engine;

namespace core_engine_tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task UsageDemonstration()
    {
        Stream bmpnData = System.IO.File.Open("path/to/bpmn/file.bpmn", FileMode.Open);
        
        var model = await BpmnModelCreator.FromStream(bmpnData);
            
        ICore core;
        await core.LoadModel(model);
        
        var initialInteractions = await core.GetInitialInteractionRequests();
        
        Assert.That(initialInteractions.Length, Is.GreaterThan(0));
        //and so on...
        
        Assert.Pass();
    }
}