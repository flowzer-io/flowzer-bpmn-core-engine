namespace StorageSystem;

public class ProcessInstanceInfo
{
    public required Guid InstanceId  { get; set; }
    public required string metaDefinitionId  { get; set; }
    public required Guid DefinitionId  { get; set; }

    public required string ProcessId  { get; set; }
    public required List<Token> Tokens  { get; set; }
    public required bool IsFinished  { get; set; }

    public required ProcessInstanceState State { get; set; }
    public required int MessageSubscriptionCount { get; set; }
    public required int SignalSubscriptionCount { get; set; }
    public required int UserTaskSubscriptionCount { get; set; }
    public required int ServiceSubscriptionCount { get; set; }

    /// <summary>
    /// Warum diese Instanz fachlich gescheitert ist — etwa ein BPMN-Fehler, den niemand gefangen
    /// hat. Bewusst nicht <c>required</c>: Bestandsdokumente kennen die Eigenschaft nicht und
    /// laden als "keine Begruendung hinterlegt".
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Die Instanz, deren Call Activity diesen Vorgang gestartet hat. Bewusst nicht
    /// <c>required</c>: Von Hand oder per Nachricht gestartete Vorgaenge haben keinen Aufrufer,
    /// und Bestandsdokumente kennen die Eigenschaft nicht.
    /// </summary>
    public Guid? ParentInstanceId { get; set; }

    /// <summary>
    /// Das wartende Call-Activity-Token in der aufrufenden Instanz. Zusammen mit
    /// <see cref="ParentInstanceId"/> benennt es den Schritt, an dem dieser Vorgang haengt.
    /// </summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>
    /// Die Versionswechsel dieser Instanz, aelteste zuerst. Bewusst nicht <c>required</c>:
    /// Bestandsdokumente kennen die Eigenschaft nicht und muessen als „nie migriert" laden.
    /// </summary>
    public List<InstanceMigrationRecord> Migrations { get; set; } = [];
}

/// <summary>
/// Ein einzelner Umzug auf eine andere Workflow-Version. Laufzeitdiagramm und Verlauf lesen
/// daran, unter welchen Versionen die Ereignisse dieser Instanz entstanden sind.
/// </summary>
public sealed record InstanceMigrationRecord(
    Guid SourceDefinitionId,
    Guid TargetDefinitionId,
    DateTimeOffset MigratedAtUtc,
    Guid MigratedByUserId);
