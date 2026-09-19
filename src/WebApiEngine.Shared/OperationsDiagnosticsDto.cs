namespace WebApiEngine.Shared;

public class OperationsDiagnosticsDto
{
    public required DateTime CheckedAtUtc { get; set; }
    public required string Environment { get; set; }
    public required OperationsStorageSnapshotDto Storage { get; set; }
    public required TimerSchedulerDiagnosticsDto TimerScheduler { get; set; }
    public required InstanceRetentionDiagnosticsDto Retention { get; set; }
    public required OperationsInstrumentationDto Instrumentation { get; set; }
    public required OperationsObservabilityDto Observability { get; set; }

    /// <summary>
    /// Zustand der mitgelieferten Konnektoren. Abgeschaltete stehen bewusst mit drin: „nicht
    /// aktiviert“ ist eine Aussage, „gar nicht aufgefuehrt“ waere keine.
    /// </summary>
    public required OperationsConnectorDto[] Connectors { get; set; }
}

/// <summary>Eine Zeile je mitgeliefertem Konnektor im Betriebsbild.</summary>
public class OperationsConnectorDto
{
    public required string Name { get; set; }
    public required string JobType { get; set; }
    public required bool Enabled { get; set; }
    public DateTime? LastRunAtUtc { get; set; }
    public required long ProcessedJobs { get; set; }
    public required long FailedJobs { get; set; }

    /// <summary>Die vom Konnektor formulierte Meldung; niemals ein aufgeloestes Secret.</summary>
    public string? LastErrorMessage { get; set; }
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

    // Abbrueche sind ein regulaerer Ausgang (Terminate-Endereignis oder die Betriebsaktion
    // „Instanz abbrechen“) und werden deshalb getrennt von den Fehlern gezaehlt. `required`
    // wie alle anderen Zaehler: Der Snapshot wird ausschliesslich im OperationsController
    // gebaut, ein vergessener Zaehler soll dort ein Compilerfehler sein.
    public required int CancelledInstances { get; set; }
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

/// <summary>
/// Zustand der Aufbewahrung beendeter Instanzen. Bewusst ohne Kennungen: Der Betrieb sieht,
/// dass und wie viel geloescht wurde, nicht welche Vorgaenge das betraf.
/// </summary>
public class InstanceRetentionDiagnosticsDto
{
    /// <summary>Ist ueberhaupt eine Frist gesetzt? Ohne Frist laeuft der Dienst nicht.</summary>
    public required bool Enabled { get; set; }

    /// <summary>Installationsweite Frist in Tagen; <c>null</c>, solange keine gesetzt ist.</summary>
    public int? Days { get; set; }

    public required int PollIntervalMinutes { get; set; }
    public required int BatchSize { get; set; }
    public required string Status { get; set; }
    public DateTime? ServiceStartedAtUtc { get; set; }
    public DateTime? LastRunStartedAtUtc { get; set; }
    public DateTime? LastRunCompletedAtUtc { get; set; }
    public DateTime? LastSuccessfulRunAtUtc { get; set; }
    public DateTime? LastFailedRunAtUtc { get; set; }
    public double? LastRunDurationMs { get; set; }

    /// <summary>Im letzten Lauf geloeschte Instanzen.</summary>
    public int LastDeletedInstances { get; set; }

    public long SuccessfulRunCount { get; set; }
    public long FailedRunCount { get; set; }
    public long TotalDeletedInstances { get; set; }

    /// <summary>Fehlermeldung des letzten Laufs; <c>null</c>, wenn der letzte Lauf trug.</summary>
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

    // Der Scrape-Endpunkt ist anonym. Der Betrieb muss deshalb auf einen Blick sehen, ob er
    // ueberhaupt offen ist und unter welchem Pfad — sonst prueft niemand, ob das Gateway ihn
    // versehentlich nach aussen durchreicht.
    public required bool PrometheusEnabled { get; set; }
    public string? PrometheusPath { get; set; }
    public required string ServiceName { get; set; }
    public required string ServiceVersion { get; set; }
}
