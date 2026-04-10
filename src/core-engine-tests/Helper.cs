using BPMN.Process;

namespace core_engine_tests;

public class Helper
{
    internal static FlowzerConfig TestFlowzerConfig { get; } = FlowzerConfig.CreateForTests();
    
    internal static async Task<InstanceEngine> StartFirstProcessOfFile(string fileName)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName,FileMode.Open));
        var process = model.GetProcesses();
        var processEngine = CreateProcessEngine(process.First());
        var instanceEngine = processEngine.StartProcess();
        return instanceEngine;
    }

    internal static ProcessEngine CreateProcessEngine(Process process)
    {
        return new ProcessEngine(process, TestFlowzerConfig);
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
