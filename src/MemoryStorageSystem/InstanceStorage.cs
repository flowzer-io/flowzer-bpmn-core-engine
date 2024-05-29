using Model;

namespace MemoryStorageSystem;

internal class InstanceStorage(EngineState engineState) : IInstanceStorage
{
    public IEnumerable<ProcessInstance> GetAllInstances() => engineState.Instances;
    public void AddInstance(ProcessInstance instance) => engineState.Instances.Add(instance);
}