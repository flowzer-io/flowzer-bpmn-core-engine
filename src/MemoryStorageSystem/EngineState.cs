using Model;

namespace MemoryStorageSystem;

internal class EngineState
{
    public List<ProcessInfo> ProcessInfos { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}