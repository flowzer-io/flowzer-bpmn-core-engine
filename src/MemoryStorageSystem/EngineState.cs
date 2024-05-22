using Model;
using StorageSystem;
using WebApiEngine.StatefulWorkflowEngine;

namespace MemoryStorageSystem;

internal class EngineState
{
    public List<ProcessInfo> ProcessInfos { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}