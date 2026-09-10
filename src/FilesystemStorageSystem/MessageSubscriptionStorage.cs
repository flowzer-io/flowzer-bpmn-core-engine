using Model;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Persistiert Message-, Signal-, User-Task- und Timer-Subscriptions als je eine JSON-Datei.
/// Schreibzugriffe sind atomar, Lesezugriffe tolerieren parallel geloeschte Dateien (siehe <see cref="StorageFile"/>).
/// </summary>
public class MessageSubscriptionStorage : IMessageSubscriptionStorage
{
    private readonly string _messageSubscriptionsPath;
    private readonly Storage _storage;

    public MessageSubscriptionStorage(Storage storage)
    {
        _storage = storage;
        _messageSubscriptionsPath = _storage.GetBasePath("FileStorage/MessageSubscriptions");

    }

    public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions()
    {
        return Task.FromResult(ReadAll<MessageSubscription>("message_*.json").Select(entry => entry.Item));
    }

    public async Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName,
        string? correlationKey, Guid? instanceId)
    {
        var allMessageSubscriptions = await GetAllMessageSubscriptions();
        var messageSubscriptions = allMessageSubscriptions.Where(x =>
            x.Message.Name == messageName &&
            x.Message.FlowzerCorrelationKey == correlationKey &&
            x.ProcessInstanceId == instanceId
        );
        return messageSubscriptions;
    }

    public async Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId)
    {
        var allMessageSubscriptions = await GetAllMessageSubscriptions();
        return allMessageSubscriptions.Where(x => x.ProcessInstanceId == instanceId);
    }

    public Task AddMessageSubscription(MessageSubscription messageSubscription)
    {
        // Letzter Riegel vor dem Dateinamen: Die API prueft die Kennung an jedem Eingang, ein
        // Katalog aus aelteren Zeiten kann aber eine ungeprueft uebernommene Kennung enthalten.
        DefinitionIdRules.EnsureValid(messageSubscription.RelatedDefinitionId);

        var randomIdOrInstanceId = messageSubscription.ProcessInstanceId ?? Guid.NewGuid();
        // Path.GetFileName: siehe InstanceStorage.AddOrUpdateInstance.
        var fullFileName = Path.Combine(_messageSubscriptionsPath,
            Path.GetFileName($"message_{messageSubscription.RelatedDefinitionId}_{randomIdOrInstanceId}.json"));
        var data = SafeStorageJson.Serialize(messageSubscription);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
    {
        foreach (var file in Directory.GetFiles(_messageSubscriptionsPath, $"message_*_{instanceId}.json"))
        {
            StorageFile.DeleteIfExists(file);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId)
    {
        foreach (var (file, subscription) in ReadAll<MessageSubscription>($"message_{metaDefinitionId}_*.json"))
        {
            if (subscription.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                StorageFile.DeleteIfExists(file);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        foreach (var (file, subscription) in ReadAll<SignalSubscription>($"signal_{relatedDefinitionId}_*.json"))
        {
            if (subscription.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                StorageFile.DeleteIfExists(file);
        }

        return Task.CompletedTask;
    }

    public void AddSignalSubscription(SignalSubscription signalSubscription)
    {
        // Letzter Riegel vor dem Dateinamen, wie bei den Nachrichten-Anmeldungen.
        DefinitionIdRules.EnsureValid(signalSubscription.RelatedDefinitionId);

        var fileIdentifier = signalSubscription.ProcessInstanceId ?? Guid.NewGuid();
        // Path.GetFileName: siehe InstanceStorage.AddOrUpdateInstance.
        var fullFileName = Path.Combine(_messageSubscriptionsPath,
            Path.GetFileName($"signal_{signalSubscription.RelatedDefinitionId}_{fileIdentifier}.json"));
        var data = SafeStorageJson.Serialize(signalSubscription);
        StorageFile.WriteAllTextAtomic(fullFileName, data);
    }

    public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId)
    {
        var subscriptions = ReadAll<SignalSubscription>("signal_*.json")
            .Select(entry => entry.Item)
            .Where(subscription => subscription.ProcessInstanceId == instanceId);

        return Task.FromResult(subscriptions);
    }

    public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
    {
        foreach (var (file, subscription) in ReadAll<SignalSubscription>("signal_*.json"))
        {
            if (subscription.ProcessInstanceId == instanceId)
            {
                StorageFile.DeleteIfExists(file);
            }
        }
    }

    public async Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId)
    {
        var instance = await TryGetInstance(instanceId);
        if (instance is null) return [];
        return ReadUserTaskDocuments("usertask_*.json")
            .Where(entry => entry.Document.ProcessInstanceId == instanceId)
            .Select(entry => Hydrate(entry.Document, instance))
            .Where(subscription => subscription is not null)
            .Cast<UserTaskSubscription>()
            .ToList();
    }

    public async Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
    {
        var ret = new List<ExtendedUserTaskSubscription>();
        var instances = new Dictionary<Guid, ProcessInstanceInfo?>();
        foreach (var (_, document) in ReadUserTaskDocuments("usertask_*.json"))
        {
            if (document.ProcessInstanceId is not { } instanceId) continue;
            if (!instances.TryGetValue(instanceId, out var instance))
            {
                instance = await TryGetInstance(instanceId);
                instances[instanceId] = instance;
            }
            if (instance is null) continue;
            var userTaskSubscription = Hydrate(document, instance);
            if (userTaskSubscription is null) continue;
            var metaDefinition = await _storage.DefinitionStorage.GetMetaDefinitionById(userTaskSubscription.MetaDefinitionId);
            var definition = await _storage.DefinitionStorage.GetDefinitionById(userTaskSubscription.DefinitionId);
            userTaskSubscription.DefinitionMetaName = metaDefinition.Name;
            userTaskSubscription.DefinitionVersion = definition.Version;

            ret.Add(userTaskSubscription);
        }

        return ret;
    }

    public async Task<ExtendedUserTaskSubscription?> GetUserTaskExtended(Guid userTaskId)
    {
        // Der Dateiname traegt die Id; ein Verzeichnislisting ist dafuer nicht noetig.
        var path = Path.Combine(_messageSubscriptionsPath, $"usertask_{userTaskId}.json");
        var content = StorageFile.ReadAllTextIfExists(path);
        if (content is null)
        {
            return null;
        }

        var document = UserTaskSubscriptionDocument.Deserialize(content);
        if (document.ProcessInstanceId is not { } instanceId) return null;
        var instance = await TryGetInstance(instanceId);
        if (instance is null) return null;
        var subscription = Hydrate(document, instance);
        if (subscription is null) return null;
        var metaDefinition = await _storage.DefinitionStorage.GetMetaDefinitionById(subscription.MetaDefinitionId);
        var definition = await _storage.DefinitionStorage.GetDefinitionById(subscription.DefinitionId);
        subscription.DefinitionMetaName = metaDefinition.Name;
        subscription.DefinitionVersion = definition.Version;

        return subscription;
    }

    internal bool UserTaskExists(Guid userTaskId) =>
        File.Exists(Path.Combine(_messageSubscriptionsPath, $"usertask_{userTaskId}.json"));

    public Task AddUserTaskSubscription(UserTaskSubscription userTasks)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"usertask_{userTasks.Id}.json");
        var data = UserTaskSubscriptionDocument.Serialize(userTasks);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId)
    {
        foreach (var file in Directory.GetFiles(_messageSubscriptionsPath, $"usertask_{userTaskSubscriptionId}.json"))
        {
            StorageFile.DeleteIfExists(file);
        }

        if (_storage.UserTaskDraftStorage is UserTaskDraftStorage drafts)
            drafts.DeleteAllFiles(userTaskSubscriptionId);
        if (_storage.UserTaskLifecycleStorage is UserTaskLifecycleStorage lifecycle)
            lifecycle.DeleteState(userTaskSubscriptionId);
        if (_storage.UserTaskDeadlineStorage is UserTaskDeadlineStorage deadlines)
            deadlines.Delete(userTaskSubscriptionId);
        if (_storage.UserTaskNotificationStorage is UserTaskNotificationStorage notifications)
            notifications.DeleteForTask(userTaskSubscriptionId);

        return Task.CompletedTask;
    }

    public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
    {
        foreach (var (file, subscription) in ReadUserTaskDocuments("usertask_*.json"))
        {
            if (subscription.ProcessInstanceId == instanceId)
            {
                StorageFile.DeleteIfExists(file);
                if (_storage.UserTaskDraftStorage is UserTaskDraftStorage drafts)
                    drafts.DeleteAllFiles(subscription.Id);
                if (_storage.UserTaskLifecycleStorage is UserTaskLifecycleStorage lifecycle)
                    lifecycle.DeleteState(subscription.Id);
                if (_storage.UserTaskDeadlineStorage is UserTaskDeadlineStorage deadlines)
                    deadlines.Delete(subscription.Id);
                if (_storage.UserTaskNotificationStorage is UserTaskNotificationStorage notifications)
                    notifications.DeleteForTask(subscription.Id);
            }
        }
    }

    public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        foreach (var (file, subscription) in ReadUserTaskDocuments($"usertask_{relatedDefinitionId}_*.json"))
        {
            if (subscription.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
            {
                StorageFile.DeleteIfExists(file);
                if (_storage.UserTaskDraftStorage is UserTaskDraftStorage drafts)
                    drafts.DeleteAllFiles(subscription.Id);
                if (_storage.UserTaskLifecycleStorage is UserTaskLifecycleStorage lifecycle)
                    lifecycle.DeleteState(subscription.Id);
                if (_storage.UserTaskDeadlineStorage is UserTaskDeadlineStorage deadlines)
                    deadlines.Delete(subscription.Id);
                if (_storage.UserTaskNotificationStorage is UserTaskNotificationStorage notifications)
                    notifications.DeleteForTask(subscription.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions()
    {
        return Task.FromResult(ReadAll<TimerSubscription>("timer_*.json").Select(entry => entry.Item));
    }

    public async Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId)
    {
        var subscriptions = await GetAllTimerSubscriptions();
        return subscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId);
    }

    public Task AddTimerSubscription(TimerSubscription timerSubscription)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"timer_{timerSubscription.Id}.json");
        var data = SafeStorageJson.Serialize(timerSubscription);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task RemoveTimerSubscription(Guid timerSubscriptionId)
    {
        foreach (var file in Directory.GetFiles(_messageSubscriptionsPath, $"timer_{timerSubscriptionId}.json"))
        {
            StorageFile.DeleteIfExists(file);
        }

        return Task.CompletedTask;
    }

    public async Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId)
    {
        var subscriptions = await GetAllTimerSubscriptions();
        foreach (var subscription in subscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId))
        {
            await RemoveTimerSubscription(subscription.Id);
        }
    }

    public async Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        var subscriptions = await GetAllTimerSubscriptions();
        foreach (var subscription in subscriptions.Where(subscription =>
                     string.Equals(subscription.RelatedDefinitionId, relatedDefinitionId, StringComparison.Ordinal) &&
                     subscription.ProcessInstanceId == null))
        {
            await RemoveTimerSubscription(subscription.Id);
        }
    }

    /// <summary>
    /// Liest alle Dateien des Suchmusters als Objekte. Dateien, die zwischen Verzeichnislisting und
    /// Lesen von einem parallelen Vorgang geloescht wurden, werden uebersprungen.
    /// </summary>
    private IEnumerable<(string File, T Item)> ReadAll<T>(string searchPattern)
    {
        foreach (var (path, content) in StorageFile.ReadExistingFiles(_messageSubscriptionsPath, searchPattern))
        {
            yield return (path, SafeStorageJson.Deserialize<T>(content));
        }
    }

    private IEnumerable<(string File, UserTaskSubscriptionDocument Document)> ReadUserTaskDocuments(
        string searchPattern)
    {
        foreach (var (path, content) in StorageFile.ReadExistingFiles(_messageSubscriptionsPath, searchPattern))
            yield return (path, UserTaskSubscriptionDocument.Deserialize(content));
    }

    private async Task<ProcessInstanceInfo?> TryGetInstance(Guid instanceId)
    {
        try { return await _storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>
    /// Bindet die schlanke Subscription an genau einen weiterhin aktiven Instanztoken.
    /// Inkonsistente oder erledigte Dateireste werden nicht als arbeitsberechtigte Aufgabe
    /// rekonstruiert.
    /// </summary>
    private static ExtendedUserTaskSubscription? Hydrate(
        UserTaskSubscriptionDocument document,
        ProcessInstanceInfo instance)
    {
        var matches = instance.Tokens.Where(token => token.Id == document.TokenId).Take(2).ToArray();
        if (matches.Length != 1
            || matches[0] is not { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask task }
            || !string.Equals(task.Id, document.FlowNodeId, StringComparison.Ordinal))
            return null;

        return document.ToSubscription(matches[0]);
    }
}
