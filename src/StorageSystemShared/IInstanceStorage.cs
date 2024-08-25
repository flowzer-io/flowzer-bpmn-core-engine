namespace StorageSystem;

public interface IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId);
    Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo);
    Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances();
}