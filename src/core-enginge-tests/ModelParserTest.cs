using System.ComponentModel.DataAnnotations;
using core_engine;

namespace core_enginge_tests;

public class ModelParserTest
{
    [Test]
    public async void Test1()
    {
        var model = await ModelParser.ParseModel(System.IO.File.Open("/embeddings/Test.bpmn",FileMode.Open));
        model = model;
    }
}