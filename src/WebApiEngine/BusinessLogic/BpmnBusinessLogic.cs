using BPMN.HumanInteraction;
using BPMN.Process;
using BPMN.Flowzer.Events;
using BPMN.Events;
using BPMN.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

using WebApiEngine.Auth;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.BusinessLogic;

public class BpmnBusinessLogic(ITransactionalStorageProvider storageProvider, ILogger<BpmnBusinessLogic>? logger = null)
{
    // Die dateibasierte Ablage kennt weder Transaktionen noch Sperren. Parallele HTTP-Requests
    // und der Timer-Scheduler wuerden sonst gleichzeitig Instanz- und Subscription-Dateien
    // lesen, loeschen und schreiben (Read-Modify-Write ohne Schutz). Alle Engine-Mutationen
    // laufen deshalb nacheinander durch diese eine Sperre; reine Lesepfade der Controller
    // bleiben davon unberuehrt. Durchsatz ist fuer eine Workflow-Engine dieser Groesse
    // unkritisch, verlorene Statuswechsel waeren es nicht.
    private readonly SemaphoreSlim _engineMutationLock = new(1, 1);

    /// <summary>
    /// Stellt die persistierten Timer wieder her und holt ueberfaellige Faelligkeiten nach.
    /// Wird beim Hochlauf vom <see cref="Background.EngineStartupService"/> aufgerufen.
    /// </summary>
    public async Task LoadAsync(bool enableTimerAutomation = true, CancellationToken cancellationToken = default)
    {
        if (!enableTimerAutomation)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RestoreInstanceTimerSubscriptions();

        // Zwischen den beiden Schritten liegt der teure Teil; ein Abbruch beim Herunterfahren
        // soll spaetestens hier greifen, statt die Faelligkeiten noch durchzuarbeiten.
        cancellationToken.ThrowIfCancellationRequested();
        await HandleTime(DateTime.UtcNow);
    }
    
    public async Task DeployDefinition(BpmnDefinition definition)
    {
        await _engineMutationLock.WaitAsync();
        try
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
        finally
        {
            _engineMutationLock.Release();
        }
    }

    private async Task UndeployDefinition(BpmnDefinition definition, ITransactionalStorage storageSystem)
    {
        await DeleteExistingSubscriptions(storageSystem, definition.DefinitionId);
    }

    /// <summary>
    /// Entfernt einen Workflow vollstaendig: Startanmeldungen (Nachrichten, Signale, Timer,
    /// User-Tasks ohne Instanzbezug), beendete Instanzen samt ihrer Anmeldungen und Auftraege,
    /// den Katalogeintrag, alle Versionen und deren BPMN-XML.
    ///
    /// Bewusst hier und nicht im Controller: Der Schritt laeuft unter derselben Sperre wie der
    /// Instanzstart. Sonst koennte zwischen der Pruefung auf laufende Instanzen und dem
    /// Loeschen noch eine Instanz starten — und stuende danach ohne ihre Definition da.
    /// Ueber die transaktionale Ablage ist das Loeschen bei PostgreSQL ausserdem ganz oder
    /// gar nicht; ein Abbruch mittendrin laesst keinen halben Katalog zurueck.
    /// </summary>
    /// <returns>Die Anzahl laufender Instanzen. Groesser als null heisst: nichts geloescht.</returns>
    public async Task<int> DeleteDefinition(string relatedDefinitionId)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();

            var activeInstances = (await storageSystem.InstanceStorage.GetAllActiveInstances())
                .Count(instance => instance.metaDefinitionId == relatedDefinitionId);
            if (activeInstances > 0)
            {
                return activeInstances;
            }

            var versions = (await storageSystem.DefinitionStorage.GetAllDefinitions())
                .Where(definition => definition.DefinitionId == relatedDefinitionId)
                .ToArray();

            // Beendete Instanzen gehen mit. Blieben sie liegen, stuenden sie danach ohne
            // Definition in der Liste: ohne Namen, ohne abrufbares Diagramm, und ihre
            // Auftraege und Anmeldungen haetten niemanden mehr, der sie aufraeumt.
            var finishedInstances = (await storageSystem.InstanceStorage.GetAllInstances())
                .Where(instance => instance.metaDefinitionId == relatedDefinitionId)
                .ToArray();

            foreach (var instance in finishedInstances)
            {
                await storageSystem.SubscriptionStorage.RemoveProcessMessageSubscriptionsByProcessInstanceId(instance.InstanceId);
                storageSystem.SubscriptionStorage.RemoveProcessSingalSubscriptionsByProcessInstanceId(instance.InstanceId);
                storageSystem.SubscriptionStorage.RemoveAllUserTaskSubscriptionsByInstanceId(instance.InstanceId);
                await storageSystem.SubscriptionStorage.RemoveProcessTimerSubscriptionsByProcessInstanceId(instance.InstanceId);
                await storageSystem.ServiceTaskStorage.RemoveJobsByInstanceId(instance.InstanceId);
                await storageSystem.InstanceStorage.DeleteInstance(instance.InstanceId);
            }

            await DeleteExistingSubscriptions(storageSystem, relatedDefinitionId);

            // Versionen zuerst, der Katalogeintrag zuletzt. Die Dateiablage kennt keine
            // Transaktion: Bricht es dazwischen ab, bleibt ein Eintrag ohne Versionen stehen —
            // sichtbar, harmlos und ein zweiter Aufruf raeumt ihn ab. Andersherum lieferte der
            // zweite Aufruf 404, waehrend Version und XML unerreichbar liegen blieben.
            foreach (var version in versions)
            {
                await storageSystem.DefinitionStorage.DeleteBinary(version.Id);
                await storageSystem.DefinitionStorage.DeleteDefinition(version.Id);
            }

            await storageSystem.DefinitionStorage.DeleteMetaDefinition(relatedDefinitionId);

            storageSystem.CommitChanges();
            return 0;
        }
        finally
        {
            _engineMutationLock.Release();
        }
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
        await SaveServiceTasks(storageSystem, catchHandler, relatedDefinitionId, definitionId, processId, processInstanceId);
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
                    Name = userTask.Name,
                    // Die Zuweisungen aus dem Modell werden beim Anlegen festgehalten. Aendert
                    // sich spaeter eine Definition, behaelt eine laufende Aufgabe die Zuweisung,
                    // mit der sie entstanden ist.
                    Assignee = string.IsNullOrWhiteSpace(userTask.FlowzerAssignee) ? null : userTask.FlowzerAssignee.Trim(),
                    CandidateUsers = UserTaskAssignment.SplitList(userTask.FlowzerCandidateUsers),
                    CandidateGroups = UserTaskAssignment.SplitList(userTask.FlowzerCandidateGroups),
                    UserCandidates = [],
                    UserGroups = [],
                    CurrenAssignedUser = null,
                    ProcessInstanceId = processInstanceId,
                    DefinitionId = definitionId,
                    MetaDefinitionId = metaDefinitionId,
                    ProcessId = processId
                });
        }
    }

    /// <summary>
    /// Legt fuer jeden wartenden Service-Task einen Auftrag an, den ein externer Worker holen
    /// kann. Bereits vergebene Auftraege derselben Instanz behalten ihren Zustand: Ein Worker,
    /// der gerade arbeitet, darf seine Sperre nicht durch einen Zwischenstand verlieren.
    /// </summary>
    private async Task SaveServiceTasks(IStorageSystem storageSystem, ICatchHandler catchHandler, string metaDefinitionId,
        Guid definitionId, string processId, Guid? processInstanceId)
    {
        if (processInstanceId is null)
        {
            return;
        }

        var activeTokens = catchHandler.ActiveServiceTasks();
        var existing = (await storageSystem.ServiceTaskStorage.GetJobs())
            .Where(job => job.ProcessInstanceId == processInstanceId.Value)
            .ToList();

        // Auftraege zu Tokens, die nicht mehr warten, sind erledigt oder abgebrochen.
        var activeTokenIds = activeTokens.Select(token => token.Id).ToHashSet();
        foreach (var obsolete in existing.Where(job => !activeTokenIds.Contains(job.Token.Id)))
        {
            await storageSystem.ServiceTaskStorage.RemoveJob(obsolete.Id);
        }

        // Die Prozessvariablen liegen am Prozess-Token, nicht am Token des Service-Tasks.
        // Ohne sie bekaeme der Worker einen leeren Auftrag und wuesste nicht, worueber er
        // entscheiden soll — eine Vertretungspruefung ohne den Namen der Vertretung.
        // Erst hier ermittelt, weil MasterToken genau ein Wurzel-Token verlangt: Ein Prozess
        // ganz ohne wartende Service-Tasks soll daran nicht scheitern.
        var processVariables = new Lazy<Variables?>(() => catchHandler is InstanceEngine engine
            ? engine.MasterToken.Variables
            : null);

        foreach (var token in activeTokens)
        {
            if (existing.Any(job => job.Token.Id == token.Id))
            {
                continue;
            }

            var serviceTask = (BPMN.Activities.ServiceTask)token.CurrentFlowNode!;
            await storageSystem.ServiceTaskStorage.SaveJob(new ServiceTaskJob
            {
                Id = Guid.NewGuid(),
                Type = serviceTask.Implementation,
                Name = serviceTask.Name,
                Token = token,
                ProcessInstanceId = processInstanceId.Value,
                MetaDefinitionId = metaDefinitionId,
                DefinitionId = definitionId,
                ProcessId = processId,
                // Ein Modell ohne Angabe bekommt einen Versuch; sonst waere der Auftrag von
                // Anfang an unbearbeitbar.
                Retries = serviceTask.FlowzerRetries > 0 ? serviceTask.FlowzerRetries : 1,
                Variables = SelectJobVariables(processVariables.Value, token.Variables)
            });
        }
    }

    /// <summary>
    /// Bestimmt, was ein externer Worker im Auftrag zu sehen bekommt.
    ///
    /// Deklariert der Service-Task Eingaben (<c>zeebe:ioMapping</c>), stehen genau diese im
    /// Token — dann bekommt der Worker genau sie und sonst nichts. Das ist der Weg, einem
    /// fremden Dienst nur das Noetige zu geben. Ohne Deklaration bekommt er die
    /// Prozessvariablen; ohne sie wuesste er nicht, worueber er entscheiden soll.
    /// </summary>
    private static Variables? SelectJobVariables(Variables? processVariables, Variables? tokenVariables)
    {
        return tokenVariables ?? processVariables;
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
        await _engineMutationLock.WaitAsync();
        try
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
        finally
        {
            _engineMutationLock.Release();
        }
    }


    public async Task<InstanceEngine> HandleMessage(Message message)
    {
        await _engineMutationLock.WaitAsync();
        try
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
        finally
        {
            _engineMutationLock.Release();
        }
    }
    
    /// <summary>
    /// Uebernimmt das Ergebnis eines externen Workers und fuehrt den Token weiter.
    /// Laeuft wie jede andere Zustandsaenderung unter der Engine-Sperre.
    /// </summary>
    public async Task<InstanceEngine> CompleteServiceTaskJob(ServiceTaskJob job, Variables? result, Guid userId)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();

            var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(job.ProcessInstanceId);
            var instance = new InstanceEngine(processInstance.Tokens);
            instance.InstanceId = job.ProcessInstanceId;

            var activeToken = instance.GetActiveServiceTasks().SingleOrDefault(token => token.Id == job.Token.Id);
            if (activeToken is null)
            {
                throw new ArgumentException(
                    $"The service task token \"{job.Token.Id}\" is not active for process instance \"{job.ProcessInstanceId}\".",
                    nameof(job));
            }

            instance.HandleTaskResult(activeToken.Id, result, userId);
            await storageSystem.ServiceTaskStorage.RemoveJob(job.Id);
            await SaveInstance(storageSystem, instance, processInstance.metaDefinitionId, processInstance.DefinitionId, processInstance.ProcessId);
            storageSystem.CommitChanges();

            return instance;
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    public async Task<InstanceEngine> HandleUserTask(UserTaskResult userTaskResult, Guid userId)
    {
        await _engineMutationLock.WaitAsync();
        try
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
        finally
        {
            _engineMutationLock.Release();
        }
    }

    /// <summary>
    /// Bricht eine laufende Instanz ab: aktive und wartende Tokens werden terminiert, offene
    /// Subscriptions entfernt. Bereits beendete Instanzen sind ein Zustandskonflikt.
    /// Eine BPMN-Kompensation bereits ausgefuehrter Aktivitaeten findet nicht statt.
    /// </summary>
    public async Task<ProcessInstanceInfo> CancelInstance(Guid instanceId)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();
            var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
            if (processInstance.IsFinished)
            {
                throw new InvalidOperationException(
                    $"Process instance \"{instanceId}\" is already finished and cannot be cancelled.");
            }

            var instance = new InstanceEngine(processInstance.Tokens)
            {
                InstanceId = processInstance.InstanceId
            };
            instance.Cancel();

            await SaveInstance(storageSystem, instance, processInstance.metaDefinitionId, processInstance.DefinitionId, processInstance.ProcessId);
            storageSystem.CommitChanges();

            return CreateProcessInstanceInfo(processInstance.DefinitionId, processInstance.metaDefinitionId, processInstance.ProcessId, instance);
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    public async Task<ProcessInstanceInfo> StartProcessInstance(string relatedDefinitionId, string? processId = null)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relatedDefinitionId);

            using var storageSystem = storageProvider.GetTransactionalStorage();

            // Der Katalogeintrag entscheidet, ob es den Workflow gibt. Ohne diese Pruefung liesse
            // sich eine Version starten, die nach dem Loeschen des Workflows noch liegt — etwa
            // weil ein paralleles Speichern sie erst danach geschrieben hat.
            var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
            if (metaDefinitions.All(metaDefinition => metaDefinition.DefinitionId != relatedDefinitionId))
            {
                throw new InvalidOperationException(
                    $"No workflow \"{relatedDefinitionId}\" exists in the catalog.");
            }

            var deployedDefinition = await storageSystem.DefinitionStorage.GetDeployedDefinition(relatedDefinitionId)
                ?? throw new InvalidOperationException(
                    $"No deployed definition is available for workflow \"{relatedDefinitionId}\".");

            var xmlData = await storageSystem.DefinitionStorage.GetBinary(deployedDefinition.Id);
            var model = ModelParser.ParseModel(xmlData);
            var process = ResolveDirectStartProcess(model, deployedDefinition, processId);

            var processEngine = new ProcessEngine(process);
            var instance = processEngine.StartProcess();
            var processInstanceInfo = CreateProcessInstanceInfo(
                deployedDefinition.Id,
                relatedDefinitionId,
                process.Id,
                instance);

            await SaveSubscriptions(
                storageSystem,
                instance,
                relatedDefinitionId,
                deployedDefinition.Id,
                process.Id,
                instance.InstanceId);
            await storageSystem.InstanceStorage.AddOrUpdateInstance(processInstanceInfo);

            storageSystem.CommitChanges();

            return processInstanceInfo;
        }
        finally
        {
            _engineMutationLock.Release();
        }
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
    
    private async  Task AddOrUpdateInstance(Guid definitionId, string relatedDefinitionId, string processId,
        ITransactionalStorage storageSystem, InstanceEngine instance)
    {
        await storageSystem.InstanceStorage.AddOrUpdateInstance(
            CreateProcessInstanceInfo(definitionId, relatedDefinitionId, processId, instance));
    }

    private async Task RestoreInstanceTimerSubscriptions()
    {
        await _engineMutationLock.WaitAsync();
        try
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
        finally
        {
            _engineMutationLock.Release();
        }
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
            ?? throw new InvalidOperationException(
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
        var initialSchedule = TimerScheduleCalculator.CreateInitialSchedule(
            timerSubscription.DueAt,
            startEvent.TimerDefinition,
            startEvent);

        var currentSchedule = new TimerSchedule(
            timerSubscription.DueAt,
            initialSchedule.RepeatInterval,
            timerSubscription.RemainingOccurrences ?? initialSchedule.RemainingOccurrences);

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

    private static Process ResolveDirectStartProcess(
        Definitions model,
        BpmnDefinition deployedDefinition,
        string? processId)
    {
        var executableProcesses = model
            .GetProcesses()
            .Where(process => process.IsExecutable)
            .ToArray();

        if (executableProcesses.Length == 0)
        {
            throw new InvalidOperationException(
                $"The deployed definition \"{deployedDefinition.DefinitionId}\" does not contain an executable process.");
        }

        var process = string.IsNullOrWhiteSpace(processId)
            ? executableProcesses.Length switch
            {
                1 => executableProcesses[0],
                _ => throw new InvalidOperationException(
                    $"The deployed definition \"{deployedDefinition.DefinitionId}\" contains multiple executable processes. Manual UI starts currently require exactly one executable process.")
            }
            : executableProcesses.SingleOrDefault(candidate =>
                  string.Equals(candidate.Id, processId, StringComparison.Ordinal))
              ?? throw new InvalidOperationException(
                  $"The executable process \"{processId}\" was not found in deployed definition \"{deployedDefinition.DefinitionId}\".");

        var directStartFlowNodes = process.GetStartFlowNodes()
            .Where(flowNode =>
                flowNode.GetType() == typeof(StartEvent) ||
                flowNode.GetType() == typeof(BPMN.Activities.Activity))
            .ToArray();

        return directStartFlowNodes.Length switch
        {
            0 => throw new InvalidOperationException(
                $"The process \"{process.Id}\" cannot be started directly from the UI because it has no plain start event."),
            1 => process,
            _ => throw new InvalidOperationException(
                $"The process \"{process.Id}\" contains multiple plain start entries. Manual UI starts currently require exactly one plain start path.")
        };
    }

    private static ProcessInstanceInfo CreateProcessInstanceInfo(
        Guid definitionId,
        string relatedDefinitionId,
        string processId,
        InstanceEngine instance)
    {
        return new ProcessInstanceInfo
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
        };
    }



    /// <summary>
    /// Loescht ein Formular samt allen Versionen — aber nur, wenn kein deployter Workflow es
    /// benutzt. Gibt die Namen der Workflows zurueck, die es benutzen; ist die Liste leer,
    /// wurde geloescht.
    ///
    /// Laeuft unter derselben Sperre wie das Deployen. Auseinandergezogen koennte zwischen
    /// Pruefung und Loeschen ein Workflow deployt werden, der genau dieses Formular benutzt —
    /// und stuende danach ohne da.
    /// </summary>
    public async Task<IReadOnlyList<string>> DeleteFormIfUnused(Guid formId, string formName)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();

            var benutztVon = await FindDeployedWorkflowsUsingForm(storageSystem, formId, formName);
            if (benutztVon.Count > 0)
            {
                return benutztVon;
            }

            await storageSystem.FormStorage.DeleteFormMetaData(formId);
            storageSystem.CommitChanges();
            return [];
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    /// <summary>
    /// Nennt die deployten Workflows, deren Aufgaben dieses Formular benutzen.
    ///
    /// Verwiesen wird im BPMN entweder ueber den <em>Namen</em> des Formulars
    /// (<c>formKey</c>, wahlweise mit angehaengter Version als <c>Name:1.0</c>) oder ueber
    /// seine Kennung (<c>formId</c>). Beides zaehlt; beim Namen ohne Ruecksicht auf Gross-
    /// und Kleinschreibung, genauso loest <see cref="UserTaskFormResolver"/> auf.
    ///
    /// Gesucht wird nur in der jeweils deployten Fassung: Eine aeltere Version, die niemand
    /// mehr starten kann, soll das Loeschen nicht blockieren.
    /// </summary>
    private static async Task<List<string>> FindDeployedWorkflowsUsingForm(
        ITransactionalStorage storageSystem,
        Guid formId,
        string formName)
    {
        var treffer = new List<string>();
        foreach (var metaDefinition in await storageSystem.DefinitionStorage.GetAllMetaDefinitions())
        {
            var deployed = await storageSystem.DefinitionStorage.GetDeployedDefinition(metaDefinition.DefinitionId);
            if (deployed is null)
            {
                continue;
            }

            Definitions modell;
            try
            {
                modell = ModelParser.ParseModel(await storageSystem.DefinitionStorage.GetBinary(deployed.Id));
            }
            catch (Exception)
            {
                // Ein Modell, das sich nicht lesen laesst, kann das Formular auch nicht
                // benutzen. Es soll das Loeschen nicht mit einem Fehler abbrechen.
                continue;
            }

            if (modell.GetProcesses()
                .SelectMany(prozess => prozess.FlowElements.OfType<UserTask>())
                .Any(aufgabe => BenutztFormular(aufgabe.Implementation, formId, formName)))
            {
                treffer.Add(metaDefinition.Name ?? metaDefinition.DefinitionId);
            }
        }

        return treffer;
    }

    /// <summary>Vergleicht einen Form-Key mit Name und Kennung; „Name:1.0" zaehlt mit.</summary>
    private static bool BenutztFormular(string? formKey, Guid formId, string formName)
    {
        if (string.IsNullOrWhiteSpace(formKey))
        {
            return false;
        }

        var schluessel = formKey.Trim();

        // Der Parser nimmt auch eine Kennung statt eines Namens an.
        if (Guid.TryParse(schluessel, out var verwieseneKennung) && verwieseneKennung == formId)
        {
            return true;
        }

        var trennung = schluessel.LastIndexOf(':');
        var name = trennung < 0 ? schluessel : schluessel[..trennung];
        return string.Equals(name.Trim(), formName, StringComparison.OrdinalIgnoreCase);
    }

}
