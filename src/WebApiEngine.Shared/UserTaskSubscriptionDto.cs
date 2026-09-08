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

    /// <summary>Zuweisung aus dem BPMN-Modell; leer heisst: fuer alle Zugelassenen offen.</summary>
    public string? Assignee { get; set; }

    /// <summary>Kandidaten aus dem Modell, bereits in Einzelwerte zerlegt.</summary>
    public List<string> CandidateUsers { get; set; } = [];

    /// <summary>Kandidatengruppen aus dem Modell, bereits in Einzelwerte zerlegt.</summary>
    public List<string> CandidateGroups { get; set; } = [];

    /// <summary><c>text</c> für freie Werte oder <c>directory</c> für stabile Referenzen.</summary>
    public string AssignmentMode { get; set; } = "text";

    /// <summary>Direkter bekannter Bearbeiter; nur im Verzeichnismodus belegt.</summary>
    public SubjectRefDto? DirectoryAssignee { get; set; }

    /// <summary>Bekannte Benutzerkandidaten; nur im Verzeichnismodus belegt.</summary>
    public List<SubjectRefDto> DirectoryCandidateUsers { get; set; } = [];

    /// <summary>Bekannte Kandidatengruppen; Gruppen bleiben typisierte Gruppenreferenzen.</summary>
    public List<SubjectRefDto> DirectoryCandidateGroups { get; set; } = [];
}

public class ExtendedUserTaskSubscriptionDto : UserTaskSubscriptionDto
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

    /// <summary>Tatsächlicher, revisionsgeschützter Bearbeitungszustand.</summary>
    public UserTaskWorkStateDto WorkState { get; set; } = new();
}

public sealed class UserTaskWorkStateDto
{
    public long Revision { get; set; }
    public bool Claimed { get; set; }
    public bool IsAssignedToCurrentUser { get; set; }
    public SubjectRefDto? ActualAssignee { get; set; }
    public string? ActualAssigneeDisplayName { get; set; }
    public bool CanWork { get; set; }
    public bool CanClaim { get; set; }
    public bool CanRelease { get; set; }
    public bool CanAssign { get; set; }
    public bool CanDelegate { get; set; }

}

public sealed class UserTaskClaimRequestDto
{
    public required long ExpectedRevision { get; set; }
}

public sealed class UserTaskReleaseRequestDto
{
    public required long ExpectedRevision { get; set; }
    public required string Reason { get; set; }
}

public sealed class UserTaskTransferRequestDto
{
    public required long ExpectedRevision { get; set; }
    public required SubjectRefDto Assignee { get; set; }
    public required string Reason { get; set; }
}
