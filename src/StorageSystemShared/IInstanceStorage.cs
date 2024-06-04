namespace StorageSystem;

public interface IInstanceStorage
{
    IEnumerable<ProcessInstance> GetAllInstances();
    void AddInstance(ProcessInstance instance);
    ProcessInstance GetInstanceById(Guid instanceId);
}