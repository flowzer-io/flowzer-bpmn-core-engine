namespace StorageSystem;

public interface IStorageSystem
{
    IProcessStorage ProcessStorage { get; }
    IMessageSubscriptionStorage MessageSubscriptionStorage { get; }
}