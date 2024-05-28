namespace MemoryStorageSystem;

public class MemoryStorageSystem : IStorageSystem
{
    private static EngineState EngineState { get; } = new();
    
    public IProcessStorage ProcessStorage { get; } = new ProcessStorage(EngineState);
    public IMessageSubscriptionStorage MessageSubscriptionStorage { get; } = new MessageSubscriptionStorage(EngineState);
}