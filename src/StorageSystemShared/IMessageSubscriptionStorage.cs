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
    Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId);
    void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId);
    

    #endregion
    
    
    #region UserTaskss

    Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId);
    
    Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId);

    /// <summary>
    /// Liefert genau eine angereicherte User-Task-Subscription oder <c>null</c>.
    ///
    /// Die Standardimplementierung filtert die Liste. Sie traegt nur, solange
    /// <see cref="GetAllUserTasksExtended"/> ihr Argument ignoriert, was beide mitgelieferten
    /// Ablagen tun; sonst suchte sie im Bestand einer nicht existierenden Person. Jede Ablage,
    /// die dort nach Person filtert, muss diese Methode ueberschreiben. Ablagen mit direktem
    /// Zugriff sollten das ohnehin tun, weil das Laden aller Aufgaben aller Personen linear mit
    /// dem Bestand teurer wird.
    /// </summary>
    async Task<ExtendedUserTaskSubscription?> GetUserTaskExtended(Guid userTaskId) =>
        (await GetAllUserTasksExtended(Guid.Empty)).FirstOrDefault(candidate => candidate.Id == userTaskId);
    Task AddUserTaskSubscription(UserTaskSubscription userTasks);
    Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId);

    void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId);
    
    Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId);
    #endregion

    #region Timers

    Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions();
    Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId);
    Task AddTimerSubscription(TimerSubscription timerSubscription);
    Task RemoveTimerSubscription(Guid timerSubscriptionId);
    Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId);
    Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId);

    #endregion



}
