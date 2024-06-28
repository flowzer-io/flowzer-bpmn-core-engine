using Model;
using StorageSystem;

namespace MySqlStorageSystem;

public class InstanceStorage : IInstanceStorage
{
    public InstanceStorage(StorageSystem storageSystem)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ProcessInstance> GetAllInstances()
    {
        throw new NotImplementedException();
    }

    public void AddInstance(ProcessInstance instance)
    {
        throw new NotImplementedException();
    }

    public ProcessInstance GetInstanceById(Guid instanceId)
    {
        throw new NotImplementedException();
    }
}