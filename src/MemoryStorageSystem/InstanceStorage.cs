using Model;

namespace MemoryStorageSystem;

internal class InstanceStorage(EngineState engineState) : IInstanceStorage
{
    public IEnumerable<Token> GetAllInstances() => engineState.Instances;
    public void AddInstance(ProcessInstance instance) => engineState.Instances.Add(instance);
    public ProcessInstance GetInstanceById(Guid instanceId) => engineState.Instances.Single(i => i.Id == instanceId);
}