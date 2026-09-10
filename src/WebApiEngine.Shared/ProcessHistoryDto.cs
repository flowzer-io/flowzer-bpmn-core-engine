namespace WebApiEngine.Shared;

/// <summary>
/// Datensparsame, schrittweise erweiterbare Historie einer objektberechtigten
/// Prozessinstanz. Der erste Vertragsstand enthält ausschließlich Human-Task-Aktionen.
/// </summary>
public sealed class ProcessHistoryDto
{
    public required Guid InstanceId { get; init; }
    public required IReadOnlyList<ProcessHistoryEventDto> Events { get; init; }
}

/// <summary>
/// Unveränderlicher öffentlicher Fakt aus dem Human-Task-Lifecycle. Personen-,
/// Begründungs-, Korrelations- und Formulardaten bleiben absichtlich intern.
/// </summary>
public sealed class ProcessHistoryEventDto
{
    public required Guid Id { get; init; }
    public required Guid UserTaskId { get; init; }
    public required string FlowNodeId { get; init; }
    public required string Action { get; init; }
    public required long Revision { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
