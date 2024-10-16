using BPMN.HumanInteraction;
using BPMN.Process;

namespace WebApiEngine.BusinessLogic;

public class BpmnBusinessLogic(ITransactionalStorageProvider storageProvider)
{
    
    public void Load()
    {
        //TODO: load timers   
    }
    
    public async Task DeployDefinition(BpmnDefinition definition)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();
        
        
        var xmlData = await storageSystem.DefinitionStorage.GetBinary(definition.Id);
        var model =  ModelParser.ParseModel(xmlData);
        
        
        await UndeployDefinition(definition, storageSystem);
        
        foreach (var process in model.GetProcesses())
        {
            var pe = new ProcessEngine(process);
            SaveSubscriptions(storageSystem, pe, definition.DefinitionId, definition.Id, process.Id);
        }


        var deployedDefiniton = await storageSystem.DefinitionStorage.GetDeployedDefinition(definition.DefinitionId);
        if (deployedDefiniton != null)
        {
            deployedDefiniton.IsActive = false;
            await storageSystem.DefinitionStorage.StoreDefinition(deployedDefiniton);
        }

        definition.IsActive = true;
        await storageSystem.DefinitionStorage.StoreDefinition(definition);
        
        storageSystem.CommitChanges();
        
    }

    private async Task UndeployDefinition(BpmnDefinition definition, ITransactionalStorage storageSystem)
    {
        await DeleteExistingSubscriptions(storageSystem, definition.DefinitionId);
    }

    private async Task DeleteExistingSubscriptions(ITransactionalStorage storageSystem, string relatedDefinitionId)
    {
        await storageSystem.SubscriptionStorage.RemoveAllProcessMessageSubscriptionsWithNoInstancedId(relatedDefinitionId);
        await storageSystem.SubscriptionStorage.RemoveAllProcessSignalSubscriptionsWithNoInstanceId(relatedDefinitionId);
        await storageSystem.SubscriptionStorage.RemoveAllUserTaskSubscriptionsWithNoInstanceId(relatedDefinitionId);
    }


    private void SaveSubscriptions(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, Guid definitionId, string processId, Guid? processInstanceId = null)
    {
        SaveCatchMessages(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        SaveActiveSignals(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        SaveUserTasks(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
    }

    private void SaveUserTasks(IStorageSystem storageSystem, ICatchHandler catchHandler, string metaDefinitionId, Guid definitionId, string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null) //if there are already stored user task subscriptions for this instance, remove them
            storageSystem.SubscriptionStorage.RemoveAllUserTaskSubscriptionsByInstanceId(processInstanceId.Value);
        
        foreach (var activeUserTask in catchHandler.ActiveUserTasks())
        {
            var userTask = (UserTask)activeUserTask.CurrentFlowNode!; 
            storageSystem.SubscriptionStorage.AddUserTaskSubscription(
                new UserTaskSubscription()
                {
                    Id = Guid.NewGuid(),
                    Token = activeUserTask,
                    Name = userTask.Name, //todo: add user candidates
                    UserCandidates = [], //todo: add user candidates
                    UserGroups = [], //todo: add user groups
                    CurrenAssignedUser = null,
                    ProcessInstanceId = processInstanceId,
                    DefinitionId = definitionId,
                    MetaDefinitionId = metaDefinitionId,
                    ProcessId = processId
                });
        }
    }

    private void SaveCatchMessages(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, 
        Guid definitionId, string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null) //if there are already stored catch messages subscriptions for this instance, remove them
            storageSystem.SubscriptionStorage.RemoveProcessMessageSubscriptionsByProcessInstanceId(processInstanceId.Value);
        
        foreach (var activeCatchMessage in catchHandler.ActiveCatchMessages)
        {
            storageSystem.SubscriptionStorage.AddMessageSubscription(
                new MessageSubscription(
                    activeCatchMessage,
                    processId,
                    relatedDefinitionId,
                    definitionId,
                    processInstanceId
                ));    
        }
    }    
    
    private void SaveActiveSignals(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, Guid definitionId,
        string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null) //if there are already stored signals subscriptions for this instance, remove them
            storageSystem.SubscriptionStorage.RemoveProcessSingalSubscriptionsByProcessInstanceId(processInstanceId.Value);

        
        foreach (var activeSignal in catchHandler.ActiveCatchSignals)
        {
            storageSystem.SubscriptionStorage.AddSignalSubscription(
                new SignalSubscription(
                    activeSignal,
                    processId,
                    relatedDefinitionId,
                    definitionId,   
                    processInstanceId
                ));    
        }
    }


    public async Task<InstanceEngine> HandleMessage(Message message)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        var messageSubscription =
            (await storageSystem.SubscriptionStorage
                .GetMessageSubscription(message.Name, message.CorrelationKey, message.InstanceId))
            .FirstOrDefault();

        if (messageSubscription is null)
            throw new Exception($"No process instance is waiting for a message with the name \"{message.Name}\" and correlation key \"{message.CorrelationKey}\" and instanceId {message.InstanceId}.");

        InstanceEngine instance;
        if (messageSubscription.ProcessInstanceId != null) //the message is for a specific instance, so load the instance
        {
            var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(messageSubscription.ProcessInstanceId.Value);
            instance = new InstanceEngine(processInstance.Tokens);
            instance.InstanceId = messageSubscription.ProcessInstanceId.Value;
            instance.HandleMessage(message);
        }
        else //the message is for a new instance, so create a new one
        {
            var xmlData = await storageSystem.DefinitionStorage.GetBinary(messageSubscription.DefinitionId);
            var model =  ModelParser.ParseModel(xmlData);

            var process = model.GetProcesses().FirstOrDefault(x => x.Id == messageSubscription.ProcessId);
            if (process == null)
                throw new Exception($"No process with the id \"{messageSubscription.ProcessId}\" was found in the definition with the id \"{messageSubscription.DefinitionId}\".");
            
            instance = StartProcessByMessage(messageSubscription.DefinitionId, messageSubscription.RelatedDefinitionId, process, message);
            
        }

        await SaveInstance(storageSystem, instance, messageSubscription.RelatedDefinitionId, messageSubscription.DefinitionId, messageSubscription.ProcessId);
        storageSystem.CommitChanges();

        return instance;
    }

    private async Task SaveInstance(ITransactionalStorage storageSystem, InstanceEngine instance, string relatedDefinitionId, Guid definitionId, string processId)
    {
        SaveSubscriptions(storageSystem, instance, relatedDefinitionId, definitionId, processId, instance.InstanceId);
        await AddOrUpdateInstance(definitionId, relatedDefinitionId, processId, storageSystem, instance);
    }
    
    private InstanceEngine StartProcessByMessage(Guid definitionsId, string relatedDefinitionId,
        Process process, Message message)
    {
        var processEngine = new ProcessEngine(process);
        var instance = processEngine.HandleMessage(message);
        return instance;
    }
    
    private async Task<InstanceEngine> StartProcess(Guid definitionsId, string relatedDefinitionId,
        Process process)
    {
        
        using var storageSystem = storageProvider.GetTransactionalStorage();
        var processEngine = new ProcessEngine(process);
        var instance = processEngine.StartProcess();
        
        await AddOrUpdateInstance(definitionsId, relatedDefinitionId, process.Id, storageSystem, instance);

        storageSystem.CommitChanges();
        
        return instance;
    }

    private async  Task AddOrUpdateInstance(Guid definitionId, string relatedDefinitionId, string processId,
        ITransactionalStorage storageSystem, InstanceEngine instance)
    {
        await storageSystem.InstanceStorage.AddOrUpdateInstance(
            new ProcessInstanceInfo()
            {
                InstanceId = instance.InstanceId,
                RelatedDefinitionId = relatedDefinitionId,
                DefinitionId = definitionId,
                ProcessId = processId,
                Tokens = instance.Tokens,
                IsFinished = instance.IsFinished,
                State = instance.State,
                MessageSubscriptionCount = instance.ActiveCatchMessages.Count,
                SignalSubscriptionCount = instance.ActiveCatchSignals.Count,
                UserTaskSubscriptionCount = instance.GetActiveUserTasks().Count(),
                ServiceSubscriptionCount = instance.GetActiveServiceTasks().Count()
            });
    }
}