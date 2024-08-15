using StorageSystem;

namespace FilesystemStorageSystem;

public class InstanceStorage(Storage storage) : IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
    {
        throw new NotImplementedException();
    }

    public Task AddInstance(ProcessInstanceInfo processInstanceInfo)
    {
        throw new NotImplementedException();
    }
}