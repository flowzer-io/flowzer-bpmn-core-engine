namespace StorageSystem;

public interface IProcessStorage
{
    IEnumerable<string> GetAllProcessDefinitionIds();
    IEnumerable<ProcessInfo> GetAllProcessesInfos();
    ProcessInfo GetActiveProcessInfo(string processId);
    void AddProcessInfo(ProcessInfo processInfo);
    void DeactivateProcessInfo(string processId);
}