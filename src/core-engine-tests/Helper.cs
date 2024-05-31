using core_engine;

namespace core_engine_tests;

public class Helper
{
    
    internal static async Task<InstanceEngine> StartFirstProcessOfFile(string fileName)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName,FileMode.Open));
        var process = model.GetProcesses();
        var processEngine = new ProcessEngine(process.First());
        var instanceEngine = processEngine.StartProcess();
        return instanceEngine;
    }
}