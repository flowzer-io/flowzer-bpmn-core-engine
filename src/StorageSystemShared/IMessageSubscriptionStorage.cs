namespace StorageSystem;

public interface IMessageSubscriptionStorage
{
    #region Messages

    Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions();
    Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId);
    Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId);
    Task AddMessageSubscription(MessageSubscription messageSubscription);
    Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId);
    Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId);
    

    #endregion

    #region Signals

    Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId);
    void AddSignalSubscription(SignalSubscription signalSubscription);
    void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId);
    

    #endregion
    
    
    #region UserTaskss

    Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId);
    
    Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId);
    Task AddUserTaskSubscription(UserTaskSubscription userTasks);
    Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId);

    void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId);
    
    Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId);
    #endregion



}