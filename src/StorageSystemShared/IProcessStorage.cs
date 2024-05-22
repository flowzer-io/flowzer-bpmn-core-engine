using BPMN.Process;
using Model;

namespace StorageSystem;

public interface IProcessStorage
{
    IEnumerable<string> GetAllProcessDefinitionIds();
    IEnumerable<ProcessInfo> GetAllProcessesInfos();
    void AddProcessInfo(ProcessInfo processInfo);
    void DeactivateProcessInfo(string processId);
}