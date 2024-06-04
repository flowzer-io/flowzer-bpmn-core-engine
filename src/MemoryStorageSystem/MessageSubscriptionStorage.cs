using Model;

namespace MemoryStorageSystem;

internal class MessageSubscriptionStorage(EngineState engineState) : IMessageSubscriptionStorage
{
    public IEnumerable<MessageSubscription> GetAllMessageSubscriptions() => engineState.ActiveMessages;

    public IEnumerable<MessageSubscription> GetMessageSubscription(string messageName, string? correlationKey = null)
        => engineState.ActiveMessages.Where(x =>
            x.Message.Name == messageName && x.Message.FlowzerCorrelationKey == correlationKey);

    public void AddMessageSubscription(MessageSubscription messageSubscription) =>
        engineState.ActiveMessages.Add(messageSubscription);

    public void RemoveProcessMessageSubscriptions(string processId)
        => engineState.ActiveMessages.RemoveAll(x => x.ProcessId == processId);
}