using WebApiEngine.Shared;

namespace WebApiEngine.Diagnostics;

/// <summary>
/// Hält den letzten bekannten Zustand des Timer-Schedulers für die Diagnose-API.
/// </summary>
public sealed class TimerSchedulerDiagnosticsState
{
    private readonly object _sync = new();
    private TimerSchedulerSnapshot _snapshot = new()
    {
        Status = "NotStarted"
    };

    public void MarkConfigured(bool enabled, int pollIntervalSeconds)
    {
        lock (_sync)
        {
            _snapshot.Enabled = enabled;
            _snapshot.PollIntervalSeconds = pollIntervalSeconds;
            if (!enabled)
            {
                _snapshot.Status = "Disabled";
            }
        }
    }

    public void MarkStarted(DateTime startedAtUtc)
    {
        lock (_sync)
        {
            _snapshot.ServiceStartedAtUtc = startedAtUtc;
            _snapshot.Status = _snapshot.Enabled ? "Starting" : "Disabled";
        }
    }

    public void MarkTickStarted(DateTime startedAtUtc)
    {
        lock (_sync)
        {
            _snapshot.LastTickStartedAtUtc = startedAtUtc;
            if (_snapshot.Enabled)
            {
                _snapshot.Status = "Running";
            }
        }
    }

    public void MarkTickSucceeded(DateTime completedAtUtc, TimeSpan duration, int processedTimers)
    {
        lock (_sync)
        {
            _snapshot.LastTickCompletedAtUtc = completedAtUtc;
            _snapshot.LastSuccessfulTickAtUtc = completedAtUtc;
            _snapshot.LastTickDurationMs = duration.TotalMilliseconds;
            _snapshot.LastProcessedTimers = processedTimers;
            _snapshot.SuccessfulTickCount++;
            _snapshot.TotalProcessedTimers += processedTimers;
            _snapshot.LastErrorMessage = null;
            _snapshot.Status = "Healthy";
        }
    }

    public void MarkTickFailed(DateTime completedAtUtc, TimeSpan duration, Exception exception)
    {
        lock (_sync)
        {
            _snapshot.LastTickCompletedAtUtc = completedAtUtc;
            _snapshot.LastFailedTickAtUtc = completedAtUtc;
            _snapshot.LastTickDurationMs = duration.TotalMilliseconds;
            _snapshot.FailedTickCount++;
            _snapshot.LastErrorMessage = exception.Message;
            _snapshot.Status = "Faulted";
        }
    }

    public TimerSchedulerDiagnosticsDto GetSnapshot()
    {
        lock (_sync)
        {
            return new TimerSchedulerDiagnosticsDto
            {
                Enabled = _snapshot.Enabled,
                PollIntervalSeconds = _snapshot.PollIntervalSeconds,
                Status = _snapshot.Status,
                ServiceStartedAtUtc = _snapshot.ServiceStartedAtUtc,
                LastTickStartedAtUtc = _snapshot.LastTickStartedAtUtc,
                LastTickCompletedAtUtc = _snapshot.LastTickCompletedAtUtc,
                LastSuccessfulTickAtUtc = _snapshot.LastSuccessfulTickAtUtc,
                LastFailedTickAtUtc = _snapshot.LastFailedTickAtUtc,
                LastTickDurationMs = _snapshot.LastTickDurationMs,
                LastProcessedTimers = _snapshot.LastProcessedTimers,
                SuccessfulTickCount = _snapshot.SuccessfulTickCount,
                FailedTickCount = _snapshot.FailedTickCount,
                TotalProcessedTimers = _snapshot.TotalProcessedTimers,
                LastErrorMessage = _snapshot.LastErrorMessage
            };
        }
    }

    private sealed class TimerSchedulerSnapshot
    {
        public bool Enabled { get; set; }
        public int PollIntervalSeconds { get; set; } = 5;
        public required string Status { get; set; }
        public DateTime? ServiceStartedAtUtc { get; set; }
        public DateTime? LastTickStartedAtUtc { get; set; }
        public DateTime? LastTickCompletedAtUtc { get; set; }
        public DateTime? LastSuccessfulTickAtUtc { get; set; }
        public DateTime? LastFailedTickAtUtc { get; set; }
        public double? LastTickDurationMs { get; set; }
        public int LastProcessedTimers { get; set; }
        public long SuccessfulTickCount { get; set; }
        public long FailedTickCount { get; set; }
        public long TotalProcessedTimers { get; set; }
        public string? LastErrorMessage { get; set; }
    }
}
