using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

public class MessageSubscriptionStorage(Storage storage) : IMessageSubscriptionStorage
{
    private readonly string _messageSubscriptionsPath = storage.GetBasePath("FileStorage/MessageSubscriptions");
    
    public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions()
    {
        return Task.FromResult(Directory.GetFiles(_messageSubscriptionsPath, "*.json").Select(file =>
        {
            var content = File.ReadAllText(file);
            var messageSubscription = JsonConvert.DeserializeObject<MessageSubscription>(content)!;
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
        var data = JsonConvert.SerializeObject(messageSubscription, Formatting.Indented);
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


    public Task RemoveProcessMessageSubscriptions(string relatedDefinitionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"message_{relatedDefinitionId}_*.json");
        foreach (var file in files)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public Task RemoveProcessSignalSubscriptions(string relatedDefinitionId)
    {
        var files = Directory.GetFiles(_messageSubscriptionsPath, $"signal_{relatedDefinitionId}_*.json");
        foreach (var file in files)
        {
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    public void AddSignalSubscription(SignalSubscription signalSubscription)
    {
        var fullFileName = Path.Combine(_messageSubscriptionsPath, $"signal_{signalSubscription.RelatedDefinitionId}_{Guid.NewGuid()}.json");
        var data = JsonConvert.SerializeObject(signalSubscription, Formatting.Indented);
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
}