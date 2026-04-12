namespace WebApiEngine.Shared;

public class OperationsDiagnosticsDto
{
    public required DateTime CheckedAtUtc { get; set; }
    public required string Environment { get; set; }
    public required OperationsStorageSnapshotDto Storage { get; set; }
    public required TimerSchedulerDiagnosticsDto TimerScheduler { get; set; }
    public required OperationsInstrumentationDto Instrumentation { get; set; }
    public required OperationsObservabilityDto Observability { get; set; }
}

public class OperationsStorageSnapshotDto
{
    public required string StorageRootHint { get; set; }
    public required int TotalDefinitions { get; set; }
    public required int ActiveDefinitions { get; set; }
    public required int DefinitionMetadataEntries { get; set; }
    public required int FormMetadataEntries { get; set; }
    public required int TotalInstances { get; set; }
    public required int ActiveInstances { get; set; }
    public required int CompletedInstances { get; set; }
    public required int FailedInstances { get; set; }
    public required int PendingMessages { get; set; }
    public required int PendingTimers { get; set; }
    public required int OpenUserTasks { get; set; }
    public required int PendingSignals { get; set; }
    public required int PendingServices { get; set; }
}

public class TimerSchedulerDiagnosticsDto
{
    public required bool Enabled { get; set; }
    public required int PollIntervalSeconds { get; set; }
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

public class OperationsInstrumentationDto
{
    public required string MeterName { get; set; }
    public required string ActivitySourceName { get; set; }
    public required string Notes { get; set; }
}

public class OperationsObservabilityDto
{
    public required bool Enabled { get; set; }
    public required bool ConsoleExporterEnabled { get; set; }
    public required bool OtlpExporterEnabled { get; set; }
    public string? OtlpEndpointHint { get; set; }
    public string? OtlpProtocol { get; set; }
    public string? OtlpHeadersHint { get; set; }
    public required string ServiceName { get; set; }
    public required string ServiceVersion { get; set; }
}
