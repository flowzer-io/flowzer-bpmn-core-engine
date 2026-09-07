namespace Model;

/// <summary>
/// Ein Ordner im Workflow-Katalog. Ordner bilden einen Baum: <see cref="ParentId"/> zeigt auf den
/// uebergeordneten Ordner, <c>null</c> bedeutet oberste Ebene.
///
/// Am Ordner haengt zugleich die Zustaendigkeit. Bisher entschied allein die globale Rolle fuers
/// Modellieren, wer aendern darf — alles oder nichts. Mit den <see cref="Assignments"/> laesst sich
/// ein Ausschnitt des Katalogs an die Menschen uebergeben, die ihn fachlich verantworten.
/// </summary>
public class WorkflowFolder
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Uebergeordneter Ordner; <c>null</c> heisst oberste Ebene.</summary>
    public Guid? ParentId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedOn { get; set; }

    public Guid CreatedByUser { get; set; }

    /// <summary>
    /// Wer in diesem Ordner etwas darf. Die Zuweisungen gelten auch fuer alle Unterordner; was
    /// weiter unten steht, kommt hinzu und nimmt nie etwas weg.
    /// </summary>
    public List<FolderAssignment> Assignments { get; set; } = [];
}

/// <summary>Was eine Zuweisung meint: eine Person oder eine Gruppe des Identity Providers.</summary>
public enum FolderSubjectKind
{
    User = 0,
    Group = 1
}

/// <summary>
/// Die beiden Rollen, die ein Ordner vergeben kann.
///
/// <see cref="Editor"/> darf die Workflows im Ordner anlegen, aendern, veroeffentlichen und
/// loeschen. <see cref="Steward"/> — die Fachverantwortung — darf zusaetzlich Unterordner anlegen
/// und die Zuweisungen selbst pflegen; das ist das eigentliche Weiterreichen.
///
/// Lesen und Starten sind bewusst nicht dabei: Beides steht in dieser Anwendung jeder
/// zugelassenen Person offen, und daran aendert ein Ordner nichts.
/// </summary>
public enum FolderRole
{
    Editor = 0,
    Steward = 1
}

/// <summary>
/// Eine einzelne Zuweisung. <see cref="Subject"/> traegt die Kennung, unter der der Identity
/// Provider die Person oder Gruppe fuehrt — dieselben Werte, mit denen ein BPMN-Modell
/// <c>assignee</c> und <c>candidateGroups</c> besetzt.
/// </summary>
public class FolderAssignment
{
    public required FolderSubjectKind SubjectKind { get; set; }

    public required string Subject { get; set; }

    public required FolderRole Role { get; set; }

    /// <summary>Anzeigename, falls bekannt. Rein fuer die Oberflaeche; geprueft wird <see cref="Subject"/>.</summary>
    public string? DisplayName { get; set; }
}
