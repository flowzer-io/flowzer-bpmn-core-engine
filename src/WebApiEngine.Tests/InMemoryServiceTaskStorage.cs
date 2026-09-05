using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Ablage der Worker-Auftraege fuer Testdoppel, die sonst keine eigene Ablage brauchen.
/// Verhaelt sich wie die echten Implementierungen, haelt aber alles im Speicher.
/// </summary>
internal sealed class InMemoryServiceTaskStorage : IServiceTaskStorage
{
    private readonly Dictionary<Guid, ServiceTaskJob> _jobs = [];
    private readonly Dictionary<Guid, ServiceTaskWebhook> _webhooks = [];

    public Task SaveJob(ServiceTaskJob job)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<ServiceTaskJob?> GetJob(Guid jobId) =>
        Task.FromResult(_jobs.GetValueOrDefault(jobId));

    public Task<IEnumerable<ServiceTaskJob>> GetJobs() =>
        Task.FromResult<IEnumerable<ServiceTaskJob>>(_jobs.Values.ToList());

    public Task<IEnumerable<ServiceTaskJob>> GetJobsByType(string type) =>
        Task.FromResult<IEnumerable<ServiceTaskJob>>(
            _jobs.Values.Where(job => string.Equals(job.Type, type, StringComparison.Ordinal)).ToList());

    public Task RemoveJob(Guid jobId)
    {
        _jobs.Remove(jobId);
        return Task.CompletedTask;
    }

    public Task RemoveJobsByInstanceId(Guid processInstanceId)
    {
        foreach (var id in _jobs.Where(entry => entry.Value.ProcessInstanceId == processInstanceId).Select(entry => entry.Key).ToList())
        {
            _jobs.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task SaveWebhook(ServiceTaskWebhook webhook)
    {
        _webhooks[webhook.Id] = webhook;
        return Task.CompletedTask;
    }

    public Task<ServiceTaskWebhook?> GetWebhook(Guid webhookId) =>
        Task.FromResult(_webhooks.GetValueOrDefault(webhookId));

    public Task<IEnumerable<ServiceTaskWebhook>> GetWebhooks() =>
        Task.FromResult<IEnumerable<ServiceTaskWebhook>>(_webhooks.Values.ToList());

    public Task RemoveWebhook(Guid webhookId)
    {
        _webhooks.Remove(webhookId);
        return Task.CompletedTask;
    }
}
