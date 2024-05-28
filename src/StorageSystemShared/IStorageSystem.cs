namespace StorageSystem;

public interface IStorageSystem
{
    IProcessStorage ProcessStorage { get; }
    IInstanceStorage InstanceStorage { get; }
    IMessageSubscriptionStorage MessageSubscriptionStorage { get; }
    
}