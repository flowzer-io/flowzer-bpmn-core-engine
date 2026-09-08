using Model;
using StorageSystem;

namespace WebApiEngine.Auth;

/// <summary>
/// Ein Abzug der Ordnerrechte einer Person zu einem Zeitpunkt: der geladene Baum und die daraus
/// abgeleitete Rolle je Ordner. Wird einmal je Anfrage gebildet, damit eine Pruefung nicht
/// wiederholt die Ablage liest und zwei Pruefungen derselben Anfrage nie widerspruechlich
/// ausfallen.
/// </summary>
public sealed class FolderPermissions
{
    private readonly IReadOnlyDictionary<Guid, FolderRole> _roles;

    public FolderPermissions(
        IReadOnlyCollection<WorkflowFolder> folders,
        IReadOnlyDictionary<Guid, FolderRole> roles,
        bool isGlobalModeler,
        DirectorySnapshot? directorySnapshot = null)
    {
        Folders = folders;
        _roles = roles;
        IsGlobalModeler = isGlobalModeler;
        DirectorySnapshot = directorySnapshot;
    }

    public IReadOnlyCollection<WorkflowFolder> Folders { get; }

    /// <summary>Traegt die Person die Anwendungsrolle fuers Modellieren? Die gilt ueberall.</summary>
    public bool IsGlobalModeler { get; }

    /// <summary>
    /// Genau der Snapshot, mit dem Directory-Zuweisungen dieser Rechteansicht ausgewertet
    /// wurden. Kontextgebundene Folgeschritte koennen so ohne Generationswechsel fortfahren.
    /// </summary>
    public DirectorySnapshot? DirectorySnapshot { get; }

    public FolderRole? RoleIn(Guid folderId) => _roles.TryGetValue(folderId, out var role) ? role : null;

    /// <summary>
    /// Darf hier ein Workflow angelegt oder geaendert werden?
    ///
    /// <c>null</c> steht fuer die oberste Ebene. Sie gehoert niemandem im Besonderen und bleibt
    /// deshalb der Anwendungsrolle vorbehalten: Wer nur einen Ordner verantwortet, soll nicht
    /// nebenbei den ganzen Katalog befuellen koennen.
    /// </summary>
    public bool MayEditIn(Guid? folderId) =>
        IsGlobalModeler || (folderId is { } id && _roles.ContainsKey(id));

    /// <summary>Darf hier ein Unterordner angelegt und die Zustaendigkeit gepflegt werden?</summary>
    public bool MayDelegateIn(Guid? folderId) =>
        IsGlobalModeler || (folderId is { } id && RoleIn(id) == FolderRole.Steward);

    /// <summary>
    /// Darf die Person ueberhaupt irgendwo schreiben?
    ///
    /// Gedacht als Vorpruefung fuer Endpunkte, die den betroffenen Ordner erst kennen, wenn sie
    /// den Anfragekoerper gelesen und ausgewertet haben — ein Definitionsupload etwa. Ohne sie
    /// beantwortete die API einer voellig unberechtigten Person zuerst, ob ihr XML gueltig ist.
    /// </summary>
    public bool MayEditAnywhere => IsGlobalModeler || _roles.Count > 0;
}
