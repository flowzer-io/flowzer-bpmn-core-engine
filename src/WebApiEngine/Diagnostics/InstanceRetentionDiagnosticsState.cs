using WebApiEngine.Shared;

namespace WebApiEngine.Diagnostics;

/// <summary>
/// Haelt den letzten bekannten Zustand der Aufbewahrung fuer die Diagnose-API. Aufgebaut wie
/// <see cref="TimerSchedulerDiagnosticsState"/>: Der Hintergrunddienst meldet, der Controller
/// liest einen Schnappschuss.
///
/// Enthaelt bewusst keine Instanzkennungen und keine Workflownamen — nur Zaehler, Zeitpunkte und
/// die Fehlermeldung des letzten Laufs.
/// </summary>
public sealed class InstanceRetentionDiagnosticsState
{
    private readonly object _sync = new();
    private readonly RetentionSnapshot _snapshot = new()
    {
        Status = "NotStarted"
    };

    public void MarkConfigured(bool enabled, int? days, int pollIntervalMinutes, int batchSize)
    {
        lock (_sync)
        {
            _snapshot.Enabled = enabled;
            _snapshot.Days = days;
            _snapshot.PollIntervalMinutes = pollIntervalMinutes;
            _snapshot.BatchSize = batchSize;
            if (!enabled) _snapshot.Status = "Disabled";
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

    public void MarkRunStarted(DateTime startedAtUtc)
    {
        lock (_sync)
        {
            _snapshot.LastRunStartedAtUtc = startedAtUtc;
            if (_snapshot.Enabled) _snapshot.Status = "Running";
        }
    }

    public void MarkRunSucceeded(DateTime completedAtUtc, TimeSpan duration, int deletedInstances)
    {
        lock (_sync)
        {
            _snapshot.LastRunCompletedAtUtc = completedAtUtc;
            _snapshot.LastSuccessfulRunAtUtc = completedAtUtc;
            _snapshot.LastRunDurationMs = duration.TotalMilliseconds;
            _snapshot.LastDeletedInstances = deletedInstances;
            _snapshot.SuccessfulRunCount++;
            _snapshot.TotalDeletedInstances += deletedInstances;
            // Ein geglueckter Lauf loescht die Meldung des vorherigen: Sonst stuende eine laengst
            // ueberholte Stoerung dauerhaft im Betriebsbild.
            _snapshot.LastErrorMessage = null;
            _snapshot.Status = "Healthy";
        }
    }

    public void MarkRunFailed(DateTime completedAtUtc, TimeSpan duration, Exception exception, int deletedInstances)
    {
        lock (_sync)
        {
            _snapshot.LastRunCompletedAtUtc = completedAtUtc;
            _snapshot.LastFailedRunAtUtc = completedAtUtc;
            _snapshot.LastRunDurationMs = duration.TotalMilliseconds;
            // Was vor dem Fehler geloescht wurde, ist geloescht und wird auch so gezaehlt.
            _snapshot.LastDeletedInstances = deletedInstances;
            _snapshot.TotalDeletedInstances += deletedInstances;
            _snapshot.FailedRunCount++;
            _snapshot.LastErrorMessage = exception.Message;
            _snapshot.Status = "Faulted";
        }
    }

    public InstanceRetentionDiagnosticsDto GetSnapshot()
    {
        lock (_sync)
        {
            return new InstanceRetentionDiagnosticsDto
            {
                Enabled = _snapshot.Enabled,
                Days = _snapshot.Days,
                PollIntervalMinutes = _snapshot.PollIntervalMinutes,
                BatchSize = _snapshot.BatchSize,
                Status = _snapshot.Status,
                ServiceStartedAtUtc = _snapshot.ServiceStartedAtUtc,
                LastRunStartedAtUtc = _snapshot.LastRunStartedAtUtc,
                LastRunCompletedAtUtc = _snapshot.LastRunCompletedAtUtc,
                LastSuccessfulRunAtUtc = _snapshot.LastSuccessfulRunAtUtc,
                LastFailedRunAtUtc = _snapshot.LastFailedRunAtUtc,
                LastRunDurationMs = _snapshot.LastRunDurationMs,
                LastDeletedInstances = _snapshot.LastDeletedInstances,
                SuccessfulRunCount = _snapshot.SuccessfulRunCount,
                FailedRunCount = _snapshot.FailedRunCount,
                TotalDeletedInstances = _snapshot.TotalDeletedInstances,
                LastErrorMessage = _snapshot.LastErrorMessage
            };
        }
    }

    private sealed class RetentionSnapshot
    {
        public bool Enabled { get; set; }
        public int? Days { get; set; }
        public int PollIntervalMinutes { get; set; } = 60;
        public int BatchSize { get; set; } = 100;
        public required string Status { get; set; }
        public DateTime? ServiceStartedAtUtc { get; set; }
        public DateTime? LastRunStartedAtUtc { get; set; }
        public DateTime? LastRunCompletedAtUtc { get; set; }
        public DateTime? LastSuccessfulRunAtUtc { get; set; }
        public DateTime? LastFailedRunAtUtc { get; set; }
        public double? LastRunDurationMs { get; set; }
        public int LastDeletedInstances { get; set; }
        public long SuccessfulRunCount { get; set; }
        public long FailedRunCount { get; set; }
        public long TotalDeletedInstances { get; set; }
        public string? LastErrorMessage { get; set; }
    }
}
