using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Persistiert Message-, Signal-, User-Task- und Timer-Subscriptions als je eine JSON-Datei.
/// Schreibzugriffe sind atomar, Lesezugriffe tolerieren parallel geloeschte Dateien (siehe <see cref="StorageFile"/>).
/// </summary>
public class MessageSubscriptionStorage : IMessageSubscriptionStorage
{
    private readonly string _messageSubscriptionsPath;
    private readonly JsonSerializerSettings _newtonSoftDefaultSettings;
    private readonly Storage _storage;

    public MessageSubscriptionStorage(Storage storage)
    {
        _storage = storage;
        _messageSubscriptionsPath = _storage.GetBasePath("FileStorage/MessageSubscriptions");
        
        _newtonSoftDefaultSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            Formatting = Formatting.Indented,
        };
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
        var randomIdOrInstanceId = messageSubscription.ProcessInstanceId ?? Guid.NewGuid();
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"message_{messageSubscription.RelatedDefinitionId}_{randomIdOrInstanceId}.json");
        var data = JsonConvert.SerializeObject(messageSubscription, _newtonSoftDefaultSettings);
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
        var fileIdentifier = signalSubscription.ProcessInstanceId ?? Guid.NewGuid();
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"signal_{signalSubscription.RelatedDefinitionId}_{fileIdentifier}.json");
        var data = JsonConvert.SerializeObject(signalSubscription, _newtonSoftDefaultSettings);
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

    public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId)
    {
        var subscriptions = ReadAll<UserTaskSubscription>("usertask_*.json")
            .Select(entry => entry.Item)
            .Where(subscription => subscription.ProcessInstanceId == instanceId)
            .ToList();

        return Task.FromResult<IEnumerable<UserTaskSubscription>>(subscriptions);
    }

    public async Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
    {
        var ret = new List<ExtendedUserTaskSubscription>();
        foreach (var (_, userTaskSubscription) in ReadAll<ExtendedUserTaskSubscription>("usertask_*.json"))
        {
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

        var subscription = JsonConvert.DeserializeObject<ExtendedUserTaskSubscription>(content, _newtonSoftDefaultSettings)!;
        var metaDefinition = await _storage.DefinitionStorage.GetMetaDefinitionById(subscription.MetaDefinitionId);
        var definition = await _storage.DefinitionStorage.GetDefinitionById(subscription.DefinitionId);
        subscription.DefinitionMetaName = metaDefinition.Name;
        subscription.DefinitionVersion = definition.Version;

        return subscription;
    }

    public Task AddUserTaskSubscription(UserTaskSubscription userTasks)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"usertask_{userTasks.Id}.json");
        var data = JsonConvert.SerializeObject(userTasks, _newtonSoftDefaultSettings);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId)
    {
        foreach (var file in Directory.GetFiles(_messageSubscriptionsPath, $"usertask_{userTaskSubscriptionId}.json"))
        {
            StorageFile.DeleteIfExists(file);
        }

        return Task.CompletedTask;
    }

    public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
    {
        foreach (var (file, subscription) in ReadAll<UserTaskSubscription>("usertask_*.json"))
        {
            if (subscription.ProcessInstanceId == instanceId)
                StorageFile.DeleteIfExists(file);
        }
    }

    public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        foreach (var (file, subscription) in ReadAll<UserTaskSubscription>($"usertask_{relatedDefinitionId}_*.json"))
        {
            if (subscription.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                StorageFile.DeleteIfExists(file);
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
        var data = JsonConvert.SerializeObject(timerSubscription, _newtonSoftDefaultSettings);
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
            var item = JsonConvert.DeserializeObject<T>(content, _newtonSoftDefaultSettings);
            if (item is not null)
            {
                yield return (path, item);
            }
        }
    }
}
