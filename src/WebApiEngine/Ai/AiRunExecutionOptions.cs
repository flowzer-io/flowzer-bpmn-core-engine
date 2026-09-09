namespace WebApiEngine.Ai;

/// <summary>Begrenzte, standardmaessig deaktivierte Takt- und Retrykonfiguration.</summary>
public sealed class AiRunExecutionOptions
{
    public const string SectionName = "AiExecution";

    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 600;
    public int HeartbeatSeconds { get; set; } = 30;
    public int RetryBaseSeconds { get; set; } = 30;
    public int MaximumRetrySeconds { get; set; } = 900;

    public bool IsValid() =>
        PollIntervalSeconds is >= 1 and <= 3600
        && BatchSize is >= 1 and <= 100
        && LeaseSeconds is >= 10 and <= 3600
        && HeartbeatSeconds is >= 1
        && HeartbeatSeconds < LeaseSeconds
        && RetryBaseSeconds is >= 1 and <= 3600
        && MaximumRetrySeconds >= RetryBaseSeconds
        && MaximumRetrySeconds <= 86_400;

    internal AiRunExecutionPolicy ToPolicy()
    {
        if (!IsValid()) throw new InvalidOperationException("AiExecution configuration is invalid.");
        return new AiRunExecutionPolicy(
            Enabled,
            TimeSpan.FromSeconds(PollIntervalSeconds),
            BatchSize,
            TimeSpan.FromSeconds(LeaseSeconds),
            TimeSpan.FromSeconds(HeartbeatSeconds),
            TimeSpan.FromSeconds(RetryBaseSeconds),
            TimeSpan.FromSeconds(MaximumRetrySeconds));
    }
}

internal sealed record AiRunExecutionPolicy(
    bool Enabled,
    TimeSpan PollInterval,
    int BatchSize,
    TimeSpan LeaseDuration,
    TimeSpan HeartbeatInterval,
    TimeSpan RetryBaseDelay,
    TimeSpan MaximumRetryDelay)
{
    public bool IsValid() =>
        PollInterval >= TimeSpan.FromSeconds(1)
        && PollInterval <= TimeSpan.FromHours(1)
        && BatchSize is >= 1 and <= 100
        && LeaseDuration >= TimeSpan.FromSeconds(10)
        && LeaseDuration <= TimeSpan.FromHours(1)
        && HeartbeatInterval >= TimeSpan.FromSeconds(1)
        && HeartbeatInterval < LeaseDuration
        && RetryBaseDelay >= TimeSpan.FromSeconds(1)
        && MaximumRetryDelay >= RetryBaseDelay
        && MaximumRetryDelay <= TimeSpan.FromDays(1);
}
