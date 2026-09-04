namespace WebApiEngine.Shared;

public class UserTaskSubscriptionDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required TokenDto Token { get; set; }
    public List<Guid> UserCandidates { get; set; } = new List<Guid>();
    public List<Guid> UserGroups { get; set; } = new List<Guid>();
    public Guid? CurrenAssignedUser { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string ProcessId { get; set; }
}

public class ExtendedUserTaskSubscriptionDto:UserTaskSubscriptionDto
{
    public string DefinitionMetaName { get; set; } = string.Empty;
    public VersionDto DefinitionVersion { get; set; } = new();

    /// <summary>
    /// Form-Key des User-Tasks aus dem BPMN-Modell (<c>zeebe:formDefinition</c>).
    /// Erlaubt optional eine Versionsangabe in der Form <c>Name:1.0</c>.
    /// Clients müssen den Wert damit nicht mehr aus dem Flow-Element auslesen.
    /// </summary>
    public string? FormKey { get; set; }

    /// <summary>Fälligkeitsangabe aus <c>zeebe:taskSchedule/@dueDate</c>.</summary>
    public string? DueDate { get; set; }

    /// <summary>Wiedervorlage aus <c>zeebe:taskSchedule/@followUpDate</c>.</summary>
    public string? FollowUpDate { get; set; }

    /// <summary>Priorität aus dem BPMN-Modell, sofern gepflegt.</summary>
    public string? Priority { get; set; }
}
