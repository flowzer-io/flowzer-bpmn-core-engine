using Model;
using StorageSystem;

namespace FilesystemStorageSystem;

public class InstanceStorage(Storage storage) : IInstanceStorage
{
    private Storage _storage = storage;

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