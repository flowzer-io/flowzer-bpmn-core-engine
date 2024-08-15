using BPMN.Process;

namespace WebApiEngine.BusinessLogic;

public class BpmnLogic(ITransactionalStorageProvider storageProvider)
{
    
    public void Load()
    {
        
    }
    
    public async Task DeployDefinition(BpmnDefinition definition)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();
        
        
        var xmlData = await storageSystem.DefinitionStorage.GetBinary(definition.Id);
        var model =  ModelParser.ParseModel(xmlData);
        
        
        UndeployDefinition(definition, storageSystem);
        
        foreach (var process in model.GetProcesses())
        {
            var pe = new ProcessEngine(process);
            SaveSubscriptions(storageSystem, pe, definition.DefinitionId, definition.Id, process.Id);
        }
        
        storageSystem.CommitChanges();
        
    }

    private void UndeployDefinition(BpmnDefinition definition, ITransactionalStorage storageSystem)
    {
        DeleteExistingSubscriptions(storageSystem, definition.DefinitionId);
    }

    private void DeleteExistingSubscriptions(ITransactionalStorage storageSystem, string relatedDefinitionId)
    {
        storageSystem.SubscriptionStorage.RemoveProcessMessageSubscriptions(relatedDefinitionId);
        storageSystem.SubscriptionStorage.RemoveProcessSignalSubscriptions(relatedDefinitionId);
    }


    private void SaveSubscriptions(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, Guid definitionId, string processId, Guid? processInstanceId = null)
    {
        SaveCatchMessages(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        SaveActiveSignals(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
    }

    private void SaveCatchMessages(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, 
        Guid definitionId, string processId, Guid? processInstanceId)
    {
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
        foreach (var activeSignal in catchHandler.ActiveCatchSignals)
        {
            storageSystem.SubscriptionStorage.AddSignalSubscription(
                new SingalSubscription(
                    activeSignal,
                    processId,
                    relatedDefinitionId,
                    definitionId,   
                    processInstanceId
                ));    
        }
    }


    public async Task HandleMessage(Message message)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        var messageSubscription = 
            (await storageSystem.SubscriptionStorage
                .GetMessageSubscription(message.Name, message.CorrelationKey))
                .FirstOrDefault() ?? 
            (await storageSystem.SubscriptionStorage.GetMessageSubscription(message.Name))
                .FirstOrDefault();

        if (messageSubscription is null)
            throw new Exception($"No process instance is waiting for a message with the name \"{message.Name}\" and correlation key \"{message.CorrelationKey}\".");

        InstanceEngine instance;
        if (messageSubscription.ProcessInstanceId != null) //the message is for a specific instance, so load the instance
        {
            var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(messageSubscription.ProcessInstanceId.Value);
            instance = new InstanceEngine(processInstance.Tokens);
        }
        else //the message is for a new instance, so create a new one
        {
            var xmlData = await storageSystem.DefinitionStorage.GetBinary(messageSubscription.DefinitionId);
            var model =  ModelParser.ParseModel(xmlData);

            var process = model.GetProcesses().FirstOrDefault(x => x.Id == messageSubscription.ProcessId);
            if (process == null)
                throw new Exception($"No process with the id \"{messageSubscription.ProcessId}\" was found in the definition with the id \"{messageSubscription.DefinitionId}\".");

            instance = StartProcess(messageSubscription.DefinitionId, process);
            
        }
        
        
        instance.HandleMessage(message);
        SaveSubscriptions(storageSystem, instance, messageSubscription.RelatedDefinitionId, messageSubscription.DefinitionId, messageSubscription.ProcessId, messageSubscription.ProcessInstanceId);

        storageSystem.CommitChanges();
    }

    private InstanceEngine StartProcess( Guid definitionsId, Process process)
    {
        
        using var storageSystem = storageProvider.GetTransactionalStorage();
        
        var processEngine = new ProcessEngine(process);
        var instance = processEngine.StartProcess();
        
        storageSystem.InstanceStorage.AddInstance(
            new ProcessInstanceInfo(
               instance.InstanceId,
               definitionsId,
               process.Id,
               instance.Tokens
            ));

        storageSystem.CommitChanges();
        
        return instance;
    }
}