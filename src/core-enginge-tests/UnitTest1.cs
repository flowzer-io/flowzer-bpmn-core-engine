using BPMN_Model.Common;
using BPMN_Model.Process;
using core_engine;

namespace core_enginge_tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
        var definitions = new Definitions()
        {
            Id = "d1"
        };
        
        var process = new Process()
        {
            Id = "p1",
            Definitions = definitions
        };
        
        definitions.Processes.Add(process);


        var engine = new NotInstantiatedProcess()
        {
            Process = process
        };
        


    }
}