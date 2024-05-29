namespace StorageSystem;

public interface IMessageSubscriptionStorage
{
    IEnumerable<MessageSubscription> GetAllMessageSubscriptions();
    IEnumerable<MessageSubscription> GetMessageSubscription(string messageName, string? correlationKey = null);
    void AddMessageSubscription(MessageSubscription messageSubscription);
    void RemoveProcessMessageSubscriptions(string processId);
}