using StorageSystem;

namespace MySqlStorageSystem;

public class StorageSystem : IStorageSystem
{
    public StorageSystem()
    {
        InstanceStorage = new InstanceStorage(this);
        MessageSubscriptionStorage = new MessageSubscriptionStorage(this);
        ProcessStorage = new ProcessStorage(this);
    }

    public IProcessStorage ProcessStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IMessageSubscriptionStorage MessageSubscriptionStorage { get; }
}