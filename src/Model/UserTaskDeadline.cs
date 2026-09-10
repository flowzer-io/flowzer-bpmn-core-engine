namespace Model;

/// <summary>
/// Einmalig gebundener Zeitvertrag einer stabilen Human Task. Die Rohwerte bleiben für
/// Diagnose und Kompatibilität erhalten; Automatisierung verwendet nur die UTC-Werte.
/// </summary>
public sealed record UserTaskDeadline
{
    public required Guid UserTaskId { get; init; }
    public required long Revision { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }
    public string? RawDueDate { get; init; }
    public string? RawFollowUpDate { get; init; }
    public required string ScheduleState { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? DueAtUtc { get; init; }
    public DateTimeOffset? FollowUpAtUtc { get; init; }
    public DateTimeOffset[] ReminderAtUtc { get; init; } = [];
    public DateTimeOffset? EscalationAtUtc { get; init; }
    public string[] EmittedMilestones { get; init; } = [];
    public DateTimeOffset? NextCheckAtUtc { get; init; }
    public required string PolicyVersion { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>Datenarme, persistente In-App-Meldung zu einem Task-Meilenstein.</summary>
public sealed record UserTaskNotification
{
    public required Guid Id { get; init; }
    public required Guid UserTaskId { get; init; }
    public required string Kind { get; init; }
    public required string DeduplicationKey { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

/// <summary>Lesequittierung ist pro authentifizierter Person vom Ereignis getrennt.</summary>
public sealed record UserTaskNotificationRead
{
    public required Guid NotificationId { get; init; }
    public required string OwnerKey { get; init; }
    public required DateTimeOffset ReadAtUtc { get; init; }
}
