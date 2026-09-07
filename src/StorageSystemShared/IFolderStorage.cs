namespace StorageSystem;

/// <summary>
/// Ablage der Workflow-Ordner. Der Baum ist klein — eine Organisation hat Dutzende Ordner, nicht
/// Millionen —, deshalb liest die Anwendung ihn am Stueck und baut die Hierarchie im Speicher.
/// Das erspart rekursive Abfragen und haelt beide Ablagen (Datei und PostgreSQL) gleich.
/// </summary>
public interface IFolderStorage
{
    Task<WorkflowFolder[]> GetAllFolders();

    Task<WorkflowFolder?> GetFolder(Guid id);

    /// <summary>Legt einen Ordner an. Ein bereits vergebenes <c>Id</c> ist ein Konflikt.</summary>
    Task StoreFolder(WorkflowFolder folder);

    Task UpdateFolder(WorkflowFolder folder);

    /// <summary>
    /// Entfernt einen Ordner. Ob er leer ist, entscheidet die Fachlogik: Die Ablage kennt die
    /// Workflows darin nicht.
    /// </summary>
    Task DeleteFolder(Guid id);
}
