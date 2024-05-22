using StorageSystem;

namespace MemoryStorageSystem;

public class MemoryStorageSystem : IStorageSystem
{
    private static EngineState EngineState { get; } = new();
    
    public IProcessStorage ProcessStorage { get; } = new ProcessStorage(EngineState);
}