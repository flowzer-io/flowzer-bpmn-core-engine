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
    /// Entfernt eine Instanz endgueltig. Wird beim Loeschen eines Workflows gebraucht: Bleiben
    /// die Datensaetze liegen, stehen sie danach ohne Definition in der Instanzliste — mit
    /// leerem Namen und ohne abrufbares Diagramm.
    ///
    /// Bewusst ohne stillen Standard: Eine Ablage, die das nicht kann, muss das melden.
    /// </summary>
    Task DeleteInstance(Guid processInstanceId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Loeschen von Instanzen nicht.");
}
