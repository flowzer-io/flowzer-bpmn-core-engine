namespace WebApiEngine.Shared;

public sealed class UserTaskDeadlineDto
{
    public string ScheduleState { get; set; } = "none";
    public string Status { get; set; } = "none";
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public DateTimeOffset? FollowUpAtUtc { get; set; }
    public DateTimeOffset? EscalationAtUtc { get; set; }
}

/// <summary>Datenarme Meldung ohne Variablen, Formulardaten oder Identity-Claims.</summary>
public sealed class NotificationDto
{
    public required Guid Id { get; set; }
    public required Guid UserTaskId { get; set; }
    public required string Kind { get; set; }
    public required DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string Severity { get; set; }
    public required string Href { get; set; }
}

