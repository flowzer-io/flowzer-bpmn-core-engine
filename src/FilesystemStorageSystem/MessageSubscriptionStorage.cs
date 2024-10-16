using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

public class MessageSubscriptionStorage : IMessageSubscriptionStorage
{
    private readonly string _messageSubscriptionsPath;
    private readonly JsonSerializerSettings _newtonSoftDefaultSettings;
    private Storage _storage;

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
        return Task.FromResult(Directory.GetFiles(_messageSubscriptionsPath, "message_*.json").Select(file =>
        {
            var content = File.ReadAllText(file);
            var messageSubscription = JsonConvert.DeserializeObject<MessageSubscription>(content, _newtonSoftDefaultSettings)!;
            return messageSubscription;
        }));
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
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"message_*_{instanceId}.json");
        foreach (var file in files)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }


    public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"message_{metaDefinitionId}_*.json");
        foreach (var file in files)
        {
            var subscription = JsonConvert.DeserializeObject<MessageSubscription>(File.ReadAllText(file),_newtonSoftDefaultSettings);
            if (subscription?.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"signal_{relatedDefinitionId}_*.json");
        foreach (var file in files)
        {
            var subscription = JsonConvert.DeserializeObject<SignalSubscription>(File.ReadAllText(file),_newtonSoftDefaultSettings);
            if (subscription?.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                File.Delete(file);
        }
        return Task.CompletedTask;
    }

    public void AddSignalSubscription(SignalSubscription signalSubscription)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"signal_{signalSubscription.RelatedDefinitionId}_{Guid.NewGuid()}.json");
        var data = JsonConvert.SerializeObject(signalSubscription, _newtonSoftDefaultSettings);
        File.WriteAllText(fullFileName, data);
    }

    public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"signal_*_{instanceId}.json");
        foreach (var file in files)
        {
            File.Delete(file);
        }
    }

    public async Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"usertask_*.json");
        var ret = new List<UserTaskSubscription>();
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            var subscription = JsonConvert.DeserializeObject<UserTaskSubscription>(content,_newtonSoftDefaultSettings)!;
            if (subscription.ProcessInstanceId == instanceId)
                ret.Add(subscription);
        }

        return ret;
    }


    public async Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"usertask_*.json");
        var ret = new List<ExtendedUserTaskSubscription>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var userTaskSubscription = JsonConvert.DeserializeObject<ExtendedUserTaskSubscription>(content,_newtonSoftDefaultSettings)!;

            var metaDefinition = await _storage.DefinitionStorage.GetMetaDefinitionById(userTaskSubscription.MetaDefinitionId);
            var definition = await _storage.DefinitionStorage.GetDefinitionById(userTaskSubscription.DefinitionId);
            userTaskSubscription.DefinitionMetaName = metaDefinition.Name;
            userTaskSubscription.DefinitionVersion = definition.Version;
            
            ret.Add(userTaskSubscription);
        }

        return ret;
    }
    
 
    public Task AddUserTaskSubscription(UserTaskSubscription userTasks)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"usertask_{userTasks.Id}.json");
        var data = JsonConvert.SerializeObject(userTasks, _newtonSoftDefaultSettings);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"usertask_{userTaskSubscriptionId}.json");
        foreach (var file in files)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"usertask_*.json");
        foreach (var file in files)
        {
            var subscription = JsonConvert.DeserializeObject<UserTaskSubscription>(File.ReadAllText(file),_newtonSoftDefaultSettings)!;
            if (subscription.ProcessInstanceId == instanceId)
                File.Delete(file);
        }
    }

    public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"usertask_{relatedDefinitionId}_*.json");
        foreach (var file in files)
        {
            var subscription = JsonConvert.DeserializeObject<UserTaskSubscription>(File.ReadAllText(file),_newtonSoftDefaultSettings);
            if (subscription?.ProcessInstanceId == null || subscription.ProcessInstanceId == Guid.Empty)
                File.Delete(file);
        }

        return Task.CompletedTask;
    }
}