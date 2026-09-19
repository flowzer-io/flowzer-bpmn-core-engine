namespace StorageSystem;

public interface IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId);

    /// <summary>
    /// Serialisiert schreibende Engine-Vorgänge derselben Instanz innerhalb der aktuellen
    /// Storage-Transaktion. Reine Lesepfade rufen diese Methode bewusst nicht auf.
    /// Ein Einzelprozess-Adapter darf sich auf seine Anwendungs-Sperre verlassen.
    /// </summary>
    Task LockForMutation(Guid processInstanceId) => Task.CompletedTask;

    Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo);
    Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances();
    Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances();

    /// <summary>
    /// Liefert ausschliesslich beendete Instanzen. Die Aufbewahrung braucht genau diese Menge;
    /// laufende Instanzen darf sie nie zu Gesicht bekommen.
    ///
    /// Der Standard filtert die Gesamtliste und traegt damit fuer jede Ablage. Eine Ablage mit
    /// eigenem Index sollte ihn ueberschreiben, damit ein Aufbewahrungslauf nicht bei jedem
    /// Durchgang den gesamten Instanzbestand laedt.
    /// </summary>
    async Task<IEnumerable<ProcessInstanceInfo>> GetAllFinishedInstances() =>
        (await GetAllInstances()).Where(instance => instance.IsFinished);

    /// <summary>
    /// Entfernt den Instanzdatensatz endgueltig — Tokens und Migrationseintraege stehen in
    /// seinem Dokument und gehen mit. Wird beim Loeschen eines Workflows gebraucht: Bleiben die
    /// Datensaetze liegen, stehen sie danach ohne Definition in der Instanzliste — mit leerem
    /// Namen und ohne abrufbares Diagramm.
    ///
    /// <b>Loescht nur die Instanz selbst.</b> Anmeldungen, Aufgabendaten, Historie, Auftraege
    /// und KI-Laeufe haengen an eigenen Ablagen und bleiben hier unberuehrt. Wer eine Instanz
    /// samt allem Angehaengten entfernen will — die Aufbewahrung und das Loeschen von Hand —
    /// nimmt <see cref="InstancePurge.ExecuteAsync"/>, nicht diese Methode.
    ///
    /// Bewusst ohne stillen Standard: Eine Ablage, die das nicht kann, muss das melden.
    /// </summary>
    Task DeleteInstance(Guid processInstanceId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Loeschen von Instanzen nicht.");
}
