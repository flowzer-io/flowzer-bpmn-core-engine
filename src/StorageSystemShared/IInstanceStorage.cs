namespace StorageSystem;

public interface IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId);
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
