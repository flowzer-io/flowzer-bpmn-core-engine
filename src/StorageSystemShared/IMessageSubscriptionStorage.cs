namespace StorageSystem;

public interface IMessageSubscriptionStorage
{
    Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions();
    Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey = null);
    Task AddMessageSubscription(MessageSubscription messageSubscription);
    Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId);
    Task RemoveProcessMessageSubscriptions(string relatedDefinitionId);
    
    Task RemoveProcessSignalSubscriptions(string relatedDefinitionId);
    void AddSignalSubscription(SingalSubscription signalSubscription);
    void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId);
}