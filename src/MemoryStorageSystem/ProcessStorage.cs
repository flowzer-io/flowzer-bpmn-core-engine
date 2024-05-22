using BPMN.Process;
using Model;
using StorageSystem;

namespace MemoryStorageSystem;

internal class ProcessStorage(EngineState engineState) : IProcessStorage
{
    public IEnumerable<string> GetAllProcessDefinitionIds()
    {
        return engineState.ProcessInfos
            .Select(m => m.Process.DefinitionsId).Distinct();
    }
    
    public IEnumerable<ProcessInfo> GetAllProcessesInfos() => engineState.ProcessInfos;

    public void AddProcessInfo(ProcessInfo processInfo)
    {
        engineState.ProcessInfos.Add(processInfo);
        
        // ToDo: Hier dann die MessageSubscription und Signale etc. hinzufügen
    }

    public void DeactivateProcessInfo(string processId)
    {
        var oldProcessInfo = engineState.ProcessInfos.SingleOrDefault(p =>
            p.Process.Id == processId && p.IsActive);
        if (oldProcessInfo is not null) oldProcessInfo.IsActive = false;
    }
}