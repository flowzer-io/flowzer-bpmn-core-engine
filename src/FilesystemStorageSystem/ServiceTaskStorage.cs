using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Aufträge und Webhook-Anmeldungen als Einzeldateien, wie die übrigen Ablagen dieser
/// Implementierung. Der Dateiname trägt die Id, damit ein Einzelzugriff ohne Listing auskommt.
/// </summary>
public class ServiceTaskStorage : IServiceTaskStorage
{
    private const string JobPrefix = "job_";
    private const string WebhookPrefix = "webhook_";

    private readonly Storage _storage;
    private readonly string _basePath;

    public ServiceTaskStorage(Storage storage)
    {
        _storage = storage;
        _basePath = storage.GetBasePath("FileStorage/ServiceTasks");
    }

    private JsonSerializerSettings Settings => _storage.NewtonSoftDefaultSettings;

    public Task SaveJob(ServiceTaskJob job)
    {
        var path = Path.Combine(_basePath, $"{JobPrefix}{job.Id}.json");
        return StorageFile.WriteAllTextAtomicAsync(path, JsonConvert.SerializeObject(job, Settings));
    }

    public Task<ServiceTaskJob?> GetJob(Guid jobId)
    {
        var content = StorageFile.ReadAllTextIfExists(Path.Combine(_basePath, $"{JobPrefix}{jobId}.json"));
        return Task.FromResult(content is null ? null : JsonConvert.DeserializeObject<ServiceTaskJob>(content, Settings));
    }

    public Task<IEnumerable<ServiceTaskJob>> GetJobs() => Task.FromResult(ReadAll());

    public Task<IEnumerable<ServiceTaskJob>> GetJobsByType(string type) =>
        Task.FromResult(ReadAll().Where(job => string.Equals(job.Type, type, StringComparison.Ordinal)));

    public Task RemoveJob(Guid jobId)
    {
        StorageFile.DeleteIfExists(Path.Combine(_basePath, $"{JobPrefix}{jobId}.json"));
        return Task.CompletedTask;
    }

    public Task RemoveJobsByInstanceId(Guid processInstanceId)
    {
        foreach (var job in ReadAll().Where(job => job.ProcessInstanceId == processInstanceId))
        {
            StorageFile.DeleteIfExists(Path.Combine(_basePath, $"{JobPrefix}{job.Id}.json"));
        }

        return Task.CompletedTask;
    }

    public Task SaveWebhook(ServiceTaskWebhook webhook)
    {
        var path = Path.Combine(_basePath, $"{WebhookPrefix}{webhook.Id}.json");
        return StorageFile.WriteAllTextAtomicAsync(path, JsonConvert.SerializeObject(webhook, Settings));
    }

    public Task<ServiceTaskWebhook?> GetWebhook(Guid webhookId)
    {
        var content = StorageFile.ReadAllTextIfExists(Path.Combine(_basePath, $"{WebhookPrefix}{webhookId}.json"));
        return Task.FromResult(content is null ? null : JsonConvert.DeserializeObject<ServiceTaskWebhook>(content, Settings));
    }

    public Task<IEnumerable<ServiceTaskWebhook>> GetWebhooks()
    {
        var webhooks = StorageFile.ReadExistingFiles(_basePath, $"{WebhookPrefix}*.json")
            .Select(entry => JsonConvert.DeserializeObject<ServiceTaskWebhook>(entry.Content, Settings)!)
            .ToList();

        return Task.FromResult<IEnumerable<ServiceTaskWebhook>>(webhooks);
    }

    public Task RemoveWebhook(Guid webhookId)
    {
        StorageFile.DeleteIfExists(Path.Combine(_basePath, $"{WebhookPrefix}{webhookId}.json"));
        return Task.CompletedTask;
    }

    private IEnumerable<ServiceTaskJob> ReadAll() =>
        StorageFile.ReadExistingFiles(_basePath, $"{JobPrefix}*.json")
            .Select(entry => JsonConvert.DeserializeObject<ServiceTaskJob>(entry.Content, Settings)!)
            .ToList();
}
