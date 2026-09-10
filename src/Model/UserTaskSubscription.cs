using BPMN.HumanInteraction;

namespace Model;

public class UserTaskSubscription
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required Token Token { get; set; }
    public List<Guid> UserCandidates { get; set; } = new List<Guid>();
    public List<Guid> UserGroups { get; set; } = new List<Guid>();
    public Guid? CurrenAssignedUser { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    
    public required string MetaDefinitionId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string ProcessId { get; set; }

    /// <summary>
    /// Zuweisung aus dem BPMN-Modell (<c>zeebe:assignmentDefinition/@assignee</c>). Ein freier Text,
    /// weil das Modell die Person unter dem Namen meint, den der Identity Provider fuehrt:
    /// Benutzername, E-Mail oder technische Id.
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>Kandidaten aus <c>@candidateUsers</c>, bereits in Einzelwerte zerlegt.</summary>
    public List<string> CandidateUsers { get; set; } = [];

    /// <summary>Kandidatengruppen aus <c>@candidateGroups</c>, bereits in Einzelwerte zerlegt.</summary>
    public List<string> CandidateGroups { get; set; } = [];

    /// <summary>
    /// Expliziter persistierter Vertrag. <c>null</c> kennzeichnet historische Datensätze,
    /// deren Modus einmalig aus dem im Token gespeicherten Modellelement nachgezogen wird.
    /// </summary>
    public UserTaskAssignmentMode? AssignmentMode { get; set; }

    /// <summary>Direkter Bearbeiter als stabile lokale Verzeichnis-Benutzer-ID.</summary>
    public Guid? DirectoryAssigneeUserId { get; set; }

    /// <summary>Kandidatenbenutzer als stabile lokale Verzeichnis-IDs.</summary>
    public List<Guid> DirectoryCandidateUserIds { get; set; } = [];

    /// <summary>Kandidatengruppen als stabile lokale Verzeichnis-IDs.</summary>
    public List<Guid> DirectoryCandidateGroupIds { get; set; } = [];
}


public class ExtendedUserTaskSubscription: UserTaskSubscription
{
    public string DefinitionMetaName { get; set; } = string.Empty;
    public Model.Version DefinitionVersion { get; set; } = new(0, 0);
}
