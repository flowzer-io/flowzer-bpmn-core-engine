namespace WebApiEngine.Shared;

/// <summary>
/// Ein Ordner des Workflow-Katalogs samt allem, was die Oberflaeche ueber ihn wissen muss:
/// die eigenen Zuweisungen, die geerbten und die daraus folgenden Rechte der aufrufenden Person.
/// Die Rechte kommen mit, damit die Oberflaeche nichts anbietet, was danach abgelehnt wird.
/// </summary>
public class WorkflowFolderDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public Guid? ParentId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedOn { get; set; }

    /// <summary>Zuweisungen, die an genau diesem Ordner haengen.</summary>
    public List<FolderAssignmentDto> Assignments { get; set; } = [];

    /// <summary>
    /// Zuweisungen aus den uebergeordneten Ordnern. Sie gelten hier ebenfalls, sind hier aber
    /// nicht aenderbar — geaendert werden sie dort, wo sie stehen.
    /// </summary>
    public List<InheritedFolderAssignmentDto> InheritedAssignments { get; set; } = [];

    /// <summary>Darf die aufrufende Person Workflows in diesem Ordner anlegen und aendern?</summary>
    public bool MayEdit { get; set; }

    /// <summary>Darf sie Unterordner anlegen und die Zuweisungen dieses Ordners pflegen?</summary>
    public bool MayDelegate { get; set; }

    /// <summary>Anzahl der Workflows unmittelbar in diesem Ordner, ohne Unterordner.</summary>
    public int WorkflowCount { get; set; }
}

/// <summary>
/// Eine Zuweisung. <c>SubjectKind</c> ist <c>user</c> oder <c>group</c>, <c>Role</c> ist
/// <c>editor</c> oder <c>steward</c> — als Zeichenketten, damit eine spaetere Rolle die
/// Reihenfolge bestehender Werte nicht verschiebt.
/// </summary>
public class FolderAssignmentDto
{
    public required string SubjectKind { get; set; }

    public required string Subject { get; set; }

    public required string Role { get; set; }

    public string? DisplayName { get; set; }
}

public class InheritedFolderAssignmentDto : FolderAssignmentDto
{
    public Guid InheritedFromId { get; set; }

    public required string InheritedFromName { get; set; }
}

/// <summary>Anlegen und Aendern eines Ordners. Beim Aendern ist <c>ParentId</c> das neue Ziel.</summary>
public class WorkflowFolderRequestDto
{
    public required string Name { get; set; }

    public Guid? ParentId { get; set; }

    public string? Description { get; set; }
}

/// <summary>Setzt die Zuweisungen eines Ordners vollstaendig neu.</summary>
public class FolderAssignmentsRequestDto
{
    public List<FolderAssignmentDto> Assignments { get; set; } = [];
}
