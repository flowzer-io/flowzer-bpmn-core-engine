using BPMN.HumanInteraction;
using BPMN.Process;
using BPMN.Flowzer.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebApiEngine.BusinessLogic;

public class BpmnBusinessLogic(ITransactionalStorageProvider storageProvider, ILogger<BpmnBusinessLogic>? logger = null)
{
    public void Load(bool enableTimerAutomation = true)
    {
        if (!enableTimerAutomation)
        {
            return;
        }

        RestoreInstanceTimerSubscriptions().GetAwaiter().GetResult();
        HandleTime(DateTime.UtcNow).GetAwaiter().GetResult();
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
            await SaveSubscriptions(storageSystem, pe, definition.DefinitionId, definition.Id, process.Id);
        }


        var deployedDefiniton = await storageSystem.DefinitionStorage.GetDeployedDefinition(definition.DefinitionId);
        if (deployedDefiniton != null)
        {
            deployedDefiniton.IsActive = false;
            await storageSystem.DefinitionStorage.StoreDefinition(deployedDefiniton);
        }

        definition.IsActive = true;
        definition.DeployedOn = DateTime.UtcNow;
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
        await storageSystem.SubscriptionStorage.RemoveAllProcessTimerSubscriptionsWithNoInstanceId(relatedDefinitionId);
    }


    private async Task SaveSubscriptions(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, Guid definitionId, string processId, Guid? processInstanceId = null)
    {
        await SaveCatchMessages(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        SaveActiveSignals(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        await SaveUserTasks(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
        await SaveActiveTimers(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
    }

    private async Task SaveUserTasks(IStorageSystem storageSystem, ICatchHandler catchHandler, string metaDefinitionId, Guid definitionId, string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null) //if there are already stored user task subscriptions for this instance, remove them
            storageSystem.SubscriptionStorage.RemoveAllUserTaskSubscriptionsByInstanceId(processInstanceId.Value);
        
        foreach (var activeUserTask in catchHandler.ActiveUserTasks())
        {
            var userTask = (UserTask)activeUserTask.CurrentFlowNode!; 
            await storageSystem.SubscriptionStorage.AddUserTaskSubscription(
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

    private async Task SaveCatchMessages(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId,
        Guid definitionId, string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null) //if there are already stored catch messages subscriptions for this instance, remove them
            await storageSystem.SubscriptionStorage.RemoveProcessMessageSubscriptionsByProcessInstanceId(processInstanceId.Value);
        
        foreach (var activeCatchMessage in catchHandler.ActiveCatchMessages)
        {
            await storageSystem.SubscriptionStorage.AddMessageSubscription(
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

    private async Task SaveActiveTimers(IStorageSystem storageSystem, ICatchHandler catchHandler, string relatedDefinitionId, Guid definitionId,
        string processId, Guid? processInstanceId)
    {
        if (processInstanceId != null)
        {
            await storageSystem.SubscriptionStorage.RemoveProcessTimerSubscriptionsByProcessInstanceId(processInstanceId.Value);
        }

        foreach (var activeTimer in catchHandler.ActiveTimerSubscriptions)
        {
            await storageSystem.SubscriptionStorage.AddTimerSubscription(new TimerSubscription
            {
                DueAt = activeTimer.DueAt,
                FlowNodeId = activeTimer.FlowNodeId,
                Kind = activeTimer.Kind,
                ProcessId = processId,
                RelatedDefinitionId = relatedDefinitionId,
                DefinitionId = definitionId,
                ProcessInstanceId = processInstanceId,
                TokenId = activeTimer.TokenId,
                RemainingOccurrences = activeTimer.RemainingOccurrences
            });
        }
    }

    public async Task<int> HandleTime(DateTime time)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        var dueTimers = (await storageSystem.SubscriptionStorage.GetAllTimerSubscriptions())
            .Where(subscription => subscription.DueAt <= time)
            .OrderBy(subscription => subscription.DueAt)
            .ToArray();

        var processedTimers = 0;

        foreach (var dueStartTimer in dueTimers.Where(subscription => subscription.ProcessInstanceId == null))
        {
            try
            {
                processedTimers += await HandleStartTimer(storageSystem, dueStartTimer, time);
            }
            catch (Exception exception)
            {
                (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogError(
                    exception,
                    "Processing start timer subscription {TimerSubscriptionId} for definition {DefinitionId} failed.",
                    dueStartTimer.Id,
                    dueStartTimer.DefinitionId);
            }
        }

        foreach (var dueInstanceTimerGroup in dueTimers
                     .Where(subscription => subscription.ProcessInstanceId != null)
                     .GroupBy(subscription => subscription.ProcessInstanceId!.Value))
        {
            try
            {
                await HandleInstanceTimers(storageSystem, dueInstanceTimerGroup.Key, time);
                processedTimers += dueInstanceTimerGroup.Count();
            }
            catch (Exception exception)
            {
                (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogError(
                    exception,
                    "Processing due timer subscriptions for instance {InstanceId} failed.",
                    dueInstanceTimerGroup.Key);
            }
        }

        if (processedTimers > 0)
        {
            storageSystem.CommitChanges();
        }

        return processedTimers;
    }


    public async Task<InstanceEngine> HandleMessage(Message message)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        var messageSubscription =
            (await storageSystem.SubscriptionStorage
                .GetMessageSubscription(message.Name, message.CorrelationKey, message.InstanceId))
            .FirstOrDefault();

        if (messageSubscription is null)
            throw new ArgumentException($"No process instance is waiting for a message with the name \"{message.Name}\" and correlation key \"{message.CorrelationKey}\" and instanceId {message.InstanceId}.");

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
                throw new FileNotFoundException($"No process with the id \"{messageSubscription.ProcessId}\" was found in the definition with the id \"{messageSubscription.DefinitionId}\".");
            
            instance = StartProcessByMessage(messageSubscription.DefinitionId, messageSubscription.RelatedDefinitionId, process, message);
            
        }

        await SaveInstance(storageSystem, instance, messageSubscription.RelatedDefinitionId, messageSubscription.DefinitionId, messageSubscription.ProcessId);
        storageSystem.CommitChanges();

        return instance;
    }
    
    public async Task<InstanceEngine> HandleUserTask(UserTaskResult userTaskResult, Guid userId)
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        if (userTaskResult.ProcessInstanceId == null)
        {
            throw new ArgumentException("User task results require a ProcessInstanceId.", nameof(userTaskResult.ProcessInstanceId));
        }

        var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(userTaskResult.ProcessInstanceId.Value);
        var instance = new InstanceEngine(processInstance.Tokens);
        instance.InstanceId = userTaskResult.ProcessInstanceId.Value;

        var activeUserTaskToken = instance.GetActiveUserTasks()
            .SingleOrDefault(token => token.Id == userTaskResult.TokenId);

        if (activeUserTaskToken == null)
        {
            throw new ArgumentException(
                $"The user task token \"{userTaskResult.TokenId}\" is not active for process instance \"{userTaskResult.ProcessInstanceId}\".",
                nameof(userTaskResult.TokenId));
        }

        if (!string.Equals(activeUserTaskToken.CurrentFlowNode?.Id, userTaskResult.FlowNodeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The user task token \"{userTaskResult.TokenId}\" does not belong to flow node \"{userTaskResult.FlowNodeId}\".",
                nameof(userTaskResult.FlowNodeId));
        }

        instance.HandleTaskResult(userTaskResult.TokenId, userTaskResult.Data, userId);
        await SaveInstance(storageSystem, instance, processInstance.metaDefinitionId, processInstance.DefinitionId, processInstance.ProcessId);
        storageSystem.CommitChanges();

        return instance;
    }

    private async Task SaveInstance(ITransactionalStorage storageSystem, InstanceEngine instance, string relatedDefinitionId, Guid definitionId, string processId)
    {
        await SaveSubscriptions(storageSystem, instance, relatedDefinitionId, definitionId, processId, instance.InstanceId);
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
                metaDefinitionId = relatedDefinitionId,
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

    private async Task RestoreInstanceTimerSubscriptions()
    {
        using var storageSystem = storageProvider.GetTransactionalStorage();

        var activeInstances = await storageSystem.InstanceStorage.GetAllActiveInstances();
        foreach (var processInstance in activeInstances)
        {
            if (!HasSingleMasterToken(processInstance))
            {
                (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogWarning(
                    "Skipping timer subscription restore for instance {InstanceId} because the stored token set has no single master token.",
                    processInstance.InstanceId);
                continue;
            }

            var instance = new InstanceEngine(processInstance.Tokens);
            instance.InstanceId = processInstance.InstanceId;
            await SaveActiveTimers(
                storageSystem,
                instance,
                processInstance.metaDefinitionId,
                processInstance.DefinitionId,
                processInstance.ProcessId,
                processInstance.InstanceId);
        }

        storageSystem.CommitChanges();
    }

    private async Task<int> HandleStartTimer(
        ITransactionalStorage storageSystem,
        TimerSubscription timerSubscription,
        DateTime time)
    {
        var xmlData = await storageSystem.DefinitionStorage.GetBinary(timerSubscription.DefinitionId);
        var model = ModelParser.ParseModel(xmlData);
        var process = model.GetProcesses().FirstOrDefault(candidate => candidate.Id == timerSubscription.ProcessId);
        if (process == null)
        {
            throw new FileNotFoundException(
                $"No process with the id \"{timerSubscription.ProcessId}\" was found in the definition with the id \"{timerSubscription.DefinitionId}\".");
        }

        var startEvent = process.FlowElements
            .OfType<FlowzerTimerStartEvent>()
            .SingleOrDefault(candidate => string.Equals(candidate.Id, timerSubscription.FlowNodeId, StringComparison.Ordinal))
            ?? throw new FileNotFoundException(
                $"No timer start event with the id \"{timerSubscription.FlowNodeId}\" was found in the process \"{timerSubscription.ProcessId}\".");

        var processedTimers = 0;
        TimerSubscription? currentTimerSubscription = timerSubscription;

        while (currentTimerSubscription != null && currentTimerSubscription.DueAt <= time)
        {
            var processEngine = new ProcessEngine(process);
            var instance = processEngine.StartProcessByTimerStartEvent(currentTimerSubscription.FlowNodeId);
            await SaveInstance(
                storageSystem,
                instance,
                currentTimerSubscription.RelatedDefinitionId,
                currentTimerSubscription.DefinitionId,
                currentTimerSubscription.ProcessId);
            processedTimers++;

            if (!TryAdvanceStartTimerSubscription(startEvent, currentTimerSubscription, out currentTimerSubscription))
            {
                currentTimerSubscription = null;
                break;
            }
        }

        await storageSystem.SubscriptionStorage.RemoveTimerSubscription(timerSubscription.Id);

        if (currentTimerSubscription != null)
        {
            await storageSystem.SubscriptionStorage.AddTimerSubscription(currentTimerSubscription);
        }

        return processedTimers;
    }

    private async Task HandleInstanceTimers(ITransactionalStorage storageSystem, Guid instanceId, DateTime time)
    {
        var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var instance = new InstanceEngine(processInstance.Tokens);
        instance.InstanceId = processInstance.InstanceId;
        instance.HandleTime(time);
        await SaveInstance(storageSystem, instance, processInstance.metaDefinitionId, processInstance.DefinitionId, processInstance.ProcessId);
    }

    private static bool HasSingleMasterToken(ProcessInstanceInfo processInstance)
    {
        return processInstance.Tokens.Count(token => token.ParentTokenId == null) == 1;
    }

    private static bool TryAdvanceStartTimerSubscription(
        FlowzerTimerStartEvent startEvent,
        TimerSubscription timerSubscription,
        out TimerSubscription? nextTimerSubscription)
    {
        var currentSchedule = new TimerSchedule(
            timerSubscription.DueAt,
            TimerScheduleCalculator.CreateInitialSchedule(
                timerSubscription.DueAt,
                startEvent.TimerDefinition,
                startEvent).RepeatInterval,
            timerSubscription.RemainingOccurrences);

        if (!TimerScheduleCalculator.TryAdvanceSchedule(currentSchedule, out var nextSchedule))
        {
            nextTimerSubscription = null;
            return false;
        }

        nextTimerSubscription = new TimerSubscription
        {
            Id = timerSubscription.Id,
            DueAt = nextSchedule.DueAt,
            FlowNodeId = timerSubscription.FlowNodeId,
            Kind = timerSubscription.Kind,
            ProcessId = timerSubscription.ProcessId,
            RelatedDefinitionId = timerSubscription.RelatedDefinitionId,
            DefinitionId = timerSubscription.DefinitionId,
            ProcessInstanceId = timerSubscription.ProcessInstanceId,
            TokenId = timerSubscription.TokenId,
            RemainingOccurrences = nextSchedule.RemainingOccurrences
        };
        return true;
    }


}
