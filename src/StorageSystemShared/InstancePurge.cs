namespace StorageSystem;

/// <summary>
/// Entfernt eine Instanz und alles, was an ihr haengt.
///
/// Bewusst an einer Stelle und gegen die Schnittstellen geschrieben, nicht je Ablage: Was zu
/// einer Instanz gehoert, ist eine fachliche Aussage und darf sich zwischen Dateiablage und
/// PostgreSQL nicht unterscheiden. Kaeme eine neue instanzgebundene Datenart hinzu und wuerde
/// nur in einem Adapter mitgeloescht, bliebe sie in der anderen Installation unbemerkt liegen —
/// genau der Datenrest, den die Aufbewahrung verhindern soll.
///
/// Reihenfolge: erst das Angehaengte, zuletzt die Instanz selbst. Bricht es bei der Dateiablage
/// mittendrin ab, fehlt hinterher Angehaengtes und die Instanz steht noch — ein zweiter Lauf
/// raeumt sie ab. Andersherum bliebe der Rest ohne Instanz liegen und niemand faende ihn wieder.
///
/// <b>Transaktion:</b> Der Aufrufer bestimmt sie. Ueber
/// <see cref="ITransactionalStorageProvider"/> laeuft der ganze Vorgang bei PostgreSQL in einer
/// Transaktion und ist damit ganz oder gar nicht. Die Dateiablage kennt keine Transaktion; dort
/// ist das Loeschen ausdruecklich best-effort (siehe docs/OPERATIONS.md, Abschnitt Aufbewahrung).
/// </summary>
public static class InstancePurge
{
    /// <summary>
    /// Loescht die Instanz samt Tokens, Anmeldungen, Aufgabendaten, Historie, Auftraegen,
    /// KI-Laeufen und Idempotenzverweisen.
    ///
    /// Eine Ablage, die eine Datenart gar nicht fuehrt, meldet das ueber
    /// <see cref="NotSupportedException"/>; das ist dann kein Datenrest, sondern eine Datenart,
    /// die es in dieser Installation nicht gibt, und wird deshalb uebersprungen.
    /// </summary>
    public static async Task ExecuteAsync(IStorageSystem storage, Guid processInstanceId)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (processInstanceId == Guid.Empty)
            throw new ArgumentException("Process instance ID is required.", nameof(processInstanceId));

        // Serialisiert gegen gleichzeitige Engine-Schreiber derselben Instanz. Ohne diese Sperre
        // koennte ein parallel laufender Vorgang die gerade geloeschte Instanz neu schreiben.
        await storage.InstanceStorage.LockForMutation(processInstanceId);

        // Anmeldungen. Die User-Task-Anmeldung nimmt Entwuerfe, Bearbeiterzustand, Faelligkeiten
        // und Meldungen mit — in PostgreSQL ueber ON DELETE CASCADE, in der Dateiablage ueber die
        // Bereinigung in RemoveAllUserTaskSubscriptionsByInstanceId.
        await storage.SubscriptionStorage.RemoveProcessMessageSubscriptionsByProcessInstanceId(processInstanceId);
        storage.SubscriptionStorage.RemoveProcessSingalSubscriptionsByProcessInstanceId(processInstanceId);
        storage.SubscriptionStorage.RemoveAllUserTaskSubscriptionsByInstanceId(processInstanceId);
        await storage.SubscriptionStorage.RemoveProcessTimerSubscriptionsByProcessInstanceId(processInstanceId);

        // Auftraege fuer externe Worker.
        await storage.ServiceTaskStorage.RemoveJobsByInstanceId(processInstanceId);

        // Historie und Ereignisspuren. Beide haengen bewusst ohne Fremdschluessel an der Instanz,
        // damit sie eine technische Bereinigung nicht unbemerkt mitnimmt. Die Aufbewahrung nimmt
        // sie ausdruecklich mit: Nach Ablauf der Frist ist die Historie kein Betriebsmittel mehr.
        await IgnoreUnsupported(() => storage.UserTaskLifecycleStorage.DeleteEventsByProcessInstance(processInstanceId));
        await IgnoreUnsupported(() => storage.RuntimeNodeEventStorage.DeleteByProcessInstance(processInstanceId));

        // KI-Laeufe dieser Instanz.
        await IgnoreUnsupported(() => storage.AiRunStorage.DeleteByProcessInstance(processInstanceId));

        // Idempotenzschluessel, die auf diese Instanz zeigen. Der Schluessel selbst traegt keine
        // Klartextdaten, wohl aber den Verweis; er verschwindet mit dem Vorgang.
        await IgnoreUnsupported(() => storage.IdempotencyStorage.DeleteByProcessInstance(processInstanceId));

        // Zuletzt die Instanz selbst. Ihre Tokens und Migrationseintraege stehen in ihrem
        // Dokument und gehen damit mit.
        await storage.InstanceStorage.DeleteInstance(processInstanceId);
    }

    /// <summary>
    /// Eine Ablage ohne diese Datenart hat auch nichts davon aufbewahrt. Nur der ausdrueckliche
    /// „kenne ich nicht"-Fall wird geschluckt; jeder andere Fehler bricht den Vorgang ab, damit
    /// ein stiller Datenrest nicht als erfolgreiche Loeschung durchgeht.
    /// </summary>
    private static async Task IgnoreUnsupported(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (NotSupportedException)
        {
        }
    }
}
