using Model;
using StorageSystem;

namespace MySqlStorageSystem;

public class MessageSubscriptionStorage : IMessageSubscriptionStorage
{
    public MessageSubscriptionStorage(StorageSystem storageSystem)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<MessageSubscription> GetAllMessageSubscriptions()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<MessageSubscription> GetMessageSubscription(string messageName, string? correlationKey = null)
    {
        throw new NotImplementedException();
    }

    public void AddMessageSubscription(MessageSubscription messageSubscription)
    {
        throw new NotImplementedException();
    }

    public void RemoveProcessMessageSubscriptions(string processId)
    {
        throw new NotImplementedException();
    }
}