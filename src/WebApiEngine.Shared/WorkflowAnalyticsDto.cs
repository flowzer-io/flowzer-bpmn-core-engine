namespace WebApiEngine.Shared;

/// <summary>
/// Auswertung der Laufzeithistorie eines Zeitraums. Die Projektion ist bewusst datensparsam:
/// Sie nennt Anzahlen und Dauern, aber keine Instanzen, keine Akteure und keine Variablen.
/// </summary>
public sealed class WorkflowAnalyticsOverviewDto
{
    /// <summary>Beginn des ausgewerteten Zeitraums, einschliesslich.</summary>
    public required DateTimeOffset FromUtc { get; init; }

    /// <summary>Ende des ausgewerteten Zeitraums, ausschliesslich.</summary>
    public required DateTimeOffset ToUtc { get; init; }

    /// <summary>Je Katalogeintrag eine Zeile, nach Gesamtzahl absteigend.</summary>
    public required IReadOnlyList<WorkflowAnalyticsSummaryDto> Workflows { get; init; }
}

/// <summary>
/// Kennzahlen eines Katalogeintrags. Eine Instanz gilt als im Zeitraum, wenn sie darin
/// gestartet wurde; der Start ist der aelteste Tokenbeginn, das Ende der juengste
/// Statuswechsel einer beendeten Instanz.
/// </summary>
public sealed class WorkflowAnalyticsSummaryDto
{
    public required string MetaDefinitionId { get; init; }

    /// <summary>Anzeigename aus dem Katalog; fehlt er, bleibt die Kennung stehen.</summary>
    public required string Name { get; init; }

    public required int TotalCount { get; init; }
    public required int RunningCount { get; init; }
    public required int CompletedCount { get; init; }
    public required int CancelledCount { get; init; }
    public required int FailedCount { get; init; }

    /// <summary>
    /// Durchlaufzeit der im Zeitraum gestarteten und inzwischen abgeschlossenen Instanzen.
    /// <c>null</c>, solange keine einzige abgeschlossen ist — eine 0 waere dort eine Aussage,
    /// die die Daten nicht tragen.
    /// </summary>
    public DurationStatisticsDto? CycleTime { get; init; }
}

/// <summary>
/// Verteilung einer Dauer in Sekunden. Perzentile werden linear interpoliert (Methode R-7),
/// der Median ist damit bei gerader Anzahl das Mittel der beiden mittleren Werte.
/// </summary>
public sealed class DurationStatisticsDto
{
    /// <summary>Anzahl der Messwerte, auf denen die Kennzahlen beruhen.</summary>
    public required int SampleCount { get; init; }

    public required double MedianSeconds { get; init; }
    public required double P90Seconds { get; init; }
    public required double MeanSeconds { get; init; }
    public required double MaxSeconds { get; init; }
}

/// <summary>Detailauswertung eines Katalogeintrags samt Engpaessen und Tagesverlauf.</summary>
public sealed class WorkflowAnalyticsDetailDto
{
    public required DateTimeOffset FromUtc { get; init; }
    public required DateTimeOffset ToUtc { get; init; }

    /// <summary>Dieselben Kennzahlen wie in der Uebersicht, hier fuer die gewaehlte Auswahl.</summary>
    public required WorkflowAnalyticsSummaryDto Summary { get; init; }

    /// <summary>
    /// Die Version, auf die eingeschraenkt wurde. <c>null</c> heisst: alle Versionen des
    /// Katalogeintrags.
    /// </summary>
    public Guid? DefinitionId { get; init; }

    /// <summary>
    /// Die Version, aus der die Knotennamen stammen: die gewaehlte, sonst die deployte.
    /// <c>null</c>, wenn keine lesbar war — dann bleiben die Namen leer statt geraten.
    /// </summary>
    public Guid? NamingDefinitionId { get; init; }

    /// <summary>Je Flow-Knoten eine Zeile, nach Median-Wartezeit absteigend (Engpaesse zuerst).</summary>
    public required IReadOnlyList<FlowNodeAnalyticsDto> Nodes { get; init; }

    /// <summary>Ein Punkt je Kalendertag (UTC) des Zeitraums, luckenlos und aufsteigend.</summary>
    public required IReadOnlyList<AnalyticsDayPointDto> Timeline { get; init; }
}

/// <summary>
/// Kennzahlen eines einzelnen Schritts. Die Wartezeit misst die Spanne zwischen dem
/// festgehaltenen Zustand <c>Active</c> und dem naechsten <c>Completed</c> oder
/// <c>Withdrawn</c> desselben Tokens am selben Knoten.
/// </summary>
public sealed class FlowNodeAnalyticsDto
{
    public required string FlowNodeId { get; init; }

    /// <summary>Name aus der benennenden Version; <c>null</c>, wenn der Knoten dort fehlt.</summary>
    public string? Name { get; init; }

    /// <summary>Anzahl der Durchlaeufe im Zeitraum, ueber alle Tokens und Instanzen.</summary>
    public required int ExecutionCount { get; init; }

    /// <summary>Tokens, die gerade an diesem Knoten stehen.</summary>
    public required int WaitingTokenCount { get; init; }

    /// <summary>
    /// Wartezeit der abgeschlossenen Durchlaeufe. <c>null</c>, wenn im Zeitraum kein
    /// Durchlauf des Knotens vollstaendig beobachtet wurde.
    /// </summary>
    public DurationStatisticsDto? WaitTime { get; init; }
}

/// <summary>Gestartete und beendete Instanzen eines einzelnen Kalendertags in UTC.</summary>
public sealed class AnalyticsDayPointDto
{
    public required DateOnly Day { get; init; }
    public required int StartedCount { get; init; }
    public required int FinishedCount { get; init; }
}
