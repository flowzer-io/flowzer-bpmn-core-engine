using Model;
using StorageSystem;

namespace FilesystemStorageSystem;

public class MessageSubscriptionyStorage(Storage storage) : IMessageSubscriptionStorage
{
    private readonly Storage _storage = storage;

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