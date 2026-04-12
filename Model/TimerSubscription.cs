namespace Model;

/// <summary>
/// Persistierbarer Laufzeiteintrag für fällige Timer in deployten Definitionen oder laufenden Instanzen.
/// </summary>
public class TimerSubscription
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required DateTime DueAt { get; init; }
    public required string FlowNodeId { get; init; }
    public required TimerSubscriptionKind Kind { get; init; }
    public required string ProcessId { get; init; }
    public required string RelatedDefinitionId { get; init; }
    public required Guid DefinitionId { get; init; }
    public Guid? ProcessInstanceId { get; init; }
    public Guid? TokenId { get; init; }
    public int? RemainingOccurrences { get; init; }
}
