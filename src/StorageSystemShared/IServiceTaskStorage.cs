namespace StorageSystem;

/// <summary>
/// Ablage der Arbeitsaufträge für externe Worker und ihrer Webhook-Anmeldungen.
/// Getrennt vom Subscription-Vertrag, weil Aufträge einen eigenen Lebenszyklus haben:
/// vergeben, gesperrt, zurückgemeldet oder gescheitert.
/// </summary>
public interface IServiceTaskStorage
{
    Task SaveJob(ServiceTaskJob job);

    /// <summary>
    /// Vergibt bis zu <paramref name="maxJobs"/> freie Auftraege des Typs an
    /// <paramref name="lockOwner"/> und liefert genau die, die dabei uebernommen wurden.
    ///
    /// Muss atomar sein: Lesen, Pruefen und Sperren in einem Schritt. Eine Vergabe aus zwei
    /// Schritten laesst zwei Aufrufer denselben Auftrag uebernehmen, und ein Service-Task mit
    /// Seiteneffekt liefe dann doppelt.
    /// </summary>
    Task<IReadOnlyList<ServiceTaskJob>> ClaimJobs(string type, string lockOwner, DateTime now, DateTime lockedUntil, int maxJobs);

    /// <summary>
    /// Liefert den Auftrag nur, wenn er <paramref name="lockOwner"/> zum Zeitpunkt
    /// <paramref name="now"/> tatsaechlich gehoert; sonst <c>null</c>.
    /// </summary>
    Task<ServiceTaskJob?> GetLockedJob(Guid jobId, string lockOwner, DateTime now);

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
