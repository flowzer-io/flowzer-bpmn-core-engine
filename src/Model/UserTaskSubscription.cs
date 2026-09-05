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
}


public class ExtendedUserTaskSubscription: UserTaskSubscription
{
    public string DefinitionMetaName { get; set; } = string.Empty;
    public Model.Version DefinitionVersion { get; set; } = new(0, 0);
}
