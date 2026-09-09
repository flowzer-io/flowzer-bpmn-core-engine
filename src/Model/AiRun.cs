namespace Model;

/// <summary>Zustand eines dauerhaften, exakt einem Engine-Token zugeordneten KI-Laufs.</summary>
public enum AiRunStatus
{
    Pending = 0,
    Running = 1,
    RetryScheduled = 2,
    ResultReady = 3,
    Completing = 4,
    Completed = 5,
    Incident = 6,
    Cancelled = 7
}

/// <summary>
/// Persistenter KI-Auftrag samt unveraenderlichem Ausfuehrungssnapshot. Secret-Werte und
/// Secret-Referenzen sind bewusst nicht Bestandteil des Modells; sie werden erst am Gateway
/// ueber die gebundene Verbindung aufgeloest.
/// </summary>
public sealed record AiRun
{
    public required Guid Id { get; init; }
    public required Guid ProcessInstanceId { get; init; }
    public required Guid TokenId { get; init; }
    public required string FlowNodeId { get; init; }
    public required string MetaDefinitionId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required string ProcessId { get; init; }

    public required Guid ConnectionId { get; init; }
    public required long ConnectionRevision { get; init; }
    public required string Model { get; init; }
    public required int InstructionVersion { get; init; }
    public required string Instruction { get; init; }
    public required string InputsJson { get; init; }
    public required string ResultSchema { get; init; }
    public required int MaxInputTokens { get; init; }
    public required int MaxOutputTokens { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required int MaximumAttempts { get; init; }

    public required AiRunStatus Status { get; init; }
    public int Attempt { get; init; }
    public required long Revision { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public DateTime? NextAttemptAtUtc { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTime? LeaseExpiresAtUtc { get; init; }
    public DateTime? ProviderCallStartedAtUtc { get; init; }

    public string? OutputJson { get; init; }
    public string? ResultModel { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
    public string? FailureCode { get; init; }
}
