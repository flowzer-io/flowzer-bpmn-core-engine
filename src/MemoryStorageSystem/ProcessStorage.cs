using core_engine;
using Model;

namespace MemoryStorageSystem;

internal class ProcessStorage(EngineState engineState) : IProcessStorage
{
    public IEnumerable<string> GetAllProcessDefinitionIds()
    {
        return engineState.ProcessInfos
            .Select(m => m.Process.DefinitionsId).Distinct();
    }
    
    public IEnumerable<ProcessInfo> GetAllProcessesInfos() => engineState.ProcessInfos;
    public ProcessInfo GetActiveProcessInfo(string processId) => 
        engineState.ProcessInfos.Single(p => p.Process.Id == processId && p.IsActive);

    public void AddProcessInfo(ProcessInfo processInfo)
    {
        engineState.ProcessInfos.Add(processInfo);
        
        engineState.ActiveMessages.AddRange(
            new ProcessEngine(processInfo.Process)
                .GetActiveCatchMessages()
                .Select(m => new MessageSubscription(m, processInfo.Process.Id)));
        // ToDo: Hier dann die Signale etc. hinzufügen
    }

    public void DeactivateProcessInfo(string processId)
    {
        var oldProcessInfo = engineState.ProcessInfos.SingleOrDefault(p =>
            p.Process.Id == processId && p.IsActive);
        if (oldProcessInfo is null) throw new Exception("The process is not found or already deactivated.");
        oldProcessInfo.IsActive = false;
        engineState.ActiveMessages.RemoveAll(x => x.ProcessId == processId && x.ProcessInstanceId == null);
    }
}