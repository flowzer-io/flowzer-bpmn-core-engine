namespace Model;

/// <summary>
/// Revisionsgeschützter Laufzeitzustand einer Human Task. Die modellierte Zuweisung bleibt
/// unverändert an der Subscription; dieser Datensatz beschreibt ausschließlich den
/// tatsächlichen Bearbeiter und bleibt nach einer Freigabe als unzugewiesene Revision erhalten.
/// </summary>
public sealed class UserTaskWorkState
{
    public required Guid UserTaskId { get; init; }
    public required long Revision { get; init; }
    public string? AssigneeOwnerKey { get; init; }
    public Guid? AssigneeUserId { get; init; }
    public Guid? DirectoryAssigneeUserId { get; init; }
    public string? AssigneeDisplayName { get; init; }
    /// <summary>Der Operator hat bewusst außerhalb der veröffentlichten Kandidatenmenge zugewiesen.</summary>
    public bool WasAssignedOutsideCandidatePool { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>Unveränderlicher Audit-Eintrag einer einzelnen Human-Task-Aktion.</summary>
public sealed class UserTaskAssignmentEvent
{
    public required Guid Id { get; init; }
    public required Guid UserTaskId { get; init; }
    public required Guid? ProcessInstanceId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required string ProcessId { get; init; }
    public required string FlowNodeId { get; init; }
    public required long Revision { get; init; }
    public required string Action { get; init; }
    public required string ActorOwnerKey { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorDisplayName { get; init; }
    public Guid? PreviousDirectoryAssigneeUserId { get; init; }
    public Guid? NextDirectoryAssigneeUserId { get; init; }
    public Guid? PreviousAssigneeUserId { get; init; }
    public Guid? NextAssigneeUserId { get; init; }
    public string? PreviousAssigneeDisplayName { get; init; }
    public string? NextAssigneeDisplayName { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
