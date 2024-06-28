using Model;
using StorageSystem;

namespace MySqlStorageSystem;

public class ProcessStorage : IProcessStorage
{
    public ProcessStorage(StorageSystem storageSystem)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetAllProcessDefinitionIds()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ProcessInfo> GetAllProcessesInfos()
    {
        throw new NotImplementedException();
    }

    public ProcessInfo GetActiveProcessInfo(string processId)
    {
        throw new NotImplementedException();
    }

    public void AddProcessInfo(ProcessInfo processInfo)
    {
        throw new NotImplementedException();
    }


    public void DeactivateProcessInfo(string processId)
    {
        throw new NotImplementedException();
    }
}