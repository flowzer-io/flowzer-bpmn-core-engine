namespace StorageSystem;

public interface IMessageSubscriptionStorage
{
    Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions();
    Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId);
    Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId);
    Task AddMessageSubscription(MessageSubscription messageSubscription);
    Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId);
    Task RemoveProcessMessageSubscriptions(string relatedDefinitionId);
    
    Task RemoveProcessSignalSubscriptions(string relatedDefinitionId);
    void AddSignalSubscription(SignalSubscription signalSubscription);
    void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId);
}