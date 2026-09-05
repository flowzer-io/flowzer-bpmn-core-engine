namespace StorageSystem;

/// <summary>
/// Ablage der Arbeitsaufträge für externe Worker und ihrer Webhook-Anmeldungen.
/// Getrennt vom Subscription-Vertrag, weil Aufträge einen eigenen Lebenszyklus haben:
/// vergeben, gesperrt, zurückgemeldet oder gescheitert.
/// </summary>
public interface IServiceTaskStorage
{
    Task SaveJob(ServiceTaskJob job);

    Task<ServiceTaskJob?> GetJob(Guid jobId);

    Task<IEnumerable<ServiceTaskJob>> GetJobs();

    Task<IEnumerable<ServiceTaskJob>> GetJobsByType(string type);

    Task RemoveJob(Guid jobId);

    Task RemoveJobsByInstanceId(Guid processInstanceId);

    Task SaveWebhook(ServiceTaskWebhook webhook);

    Task<ServiceTaskWebhook?> GetWebhook(Guid webhookId);

    Task<IEnumerable<ServiceTaskWebhook>> GetWebhooks();

    Task RemoveWebhook(Guid webhookId);
}
