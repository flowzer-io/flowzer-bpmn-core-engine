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
    /// Verlaengert eine noch gueltige Lease nur fuer ihren aktuellen Inhaber und liefert den
    /// aktualisierten Auftrag. Pruefung und Schreiben muessen atomar erfolgen; ein Heartbeat
    /// darf eine zwischenzeitlich abgelaufene oder neu vergebene Lease niemals wiederbeleben.
    /// Eine bereits spaeter endende Lease wird nicht verkuerzt.
    /// </summary>
    Task<ServiceTaskJob?> RenewJobLease(Guid jobId, string lockOwner, DateTime now, DateTime lockedUntil);

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

    /// <summary>
    /// Bindet alle Auftraege einer Instanz auf <paramref name="definitionId"/> um und liefert
    /// deren Anzahl. Es darf sich ausschliesslich die Versionsbindung aendern.
    ///
    /// Muss atomar sein und den Vergabezustand unter der Zeilensperre neu lesen: Vergabe,
    /// Heartbeat und Fehlermeldung laufen ohne die Engine-Sperre. Ein Lese-Aendern-Schreiben
    /// ueber den ganzen Auftrag ueberschriebe eine dazwischen erteilte Lease, und ein zweiter
    /// Worker bekaeme denselben Auftrag mit Seiteneffekt ein zweites Mal.
    ///
    /// Bewusst ohne stillen Standard, wie beim Loeschen einer Instanz: Eine Ablage, die
    /// Auftraege fuehrt, aber diesen Vertrag nicht kennt, liesse sie sonst an der Quellversion.
    /// </summary>
    Task<int> RebindJobsOfInstance(Guid processInstanceId, Guid definitionId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Umbinden von Auftraegen nicht.");

    Task SaveWebhook(ServiceTaskWebhook webhook);

    Task<ServiceTaskWebhook?> GetWebhook(Guid webhookId);

    Task<IEnumerable<ServiceTaskWebhook>> GetWebhooks();

    Task RemoveWebhook(Guid webhookId);
}
