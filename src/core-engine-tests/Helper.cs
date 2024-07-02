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

    public static void DoTestServiceThings(InstanceEngine instanceEngine)
    {
        
        while (true)
        {
            var tokens = instanceEngine.ActiveTokens.Where(x=>x.CurrentFlowNode is ServiceTask { Implementation: "InputAsOutput" }).ToArray();
            if (tokens.Length == 0)
                break;
            
            foreach (var token in tokens)
            {
                instanceEngine.HandleTaskResult(token.Id, token.Variables);
            }    
        }
    }
}