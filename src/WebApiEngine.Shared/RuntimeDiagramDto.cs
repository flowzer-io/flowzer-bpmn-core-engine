namespace WebApiEngine.Shared;

/// <summary>
/// Datensparsame, serverseitig autorisierte Laufzeitprojektion der beim Start gebundenen
/// Prozessversion. Das XML enthält nur die für die Darstellung nötige BPMN-Struktur.
/// </summary>
public sealed class RuntimeDiagramDto
{
    public required Guid InstanceId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required string ProcessId { get; init; }
    public required ProcessInstanceStateDto State { get; init; }
    public required DateTimeOffset SnapshotAtUtc { get; init; }
    public required string DiagramXml { get; init; }
    public required IReadOnlyList<RuntimeNodeSummaryDto> Nodes { get; init; }
    public required IReadOnlyList<RuntimeNodeEventDto> Events { get; init; }
}

/// <summary>Verdichteter aktueller Zustand aller Ausführungspfade an einem BPMN-Knoten.</summary>
public sealed class RuntimeNodeSummaryDto
{
    public required string FlowNodeId { get; init; }
    public required RuntimeNodeStatusDto Status { get; init; }
    public required int TokenCount { get; init; }
    public DateTimeOffset? LastChangedAtUtc { get; init; }
}

/// <summary>
/// Unveränderlicher Laufzeitfakt ohne Token-, Korrelations-, Personen- oder Formulardaten.
/// Die Reihenfolge entspricht der kanonischen Sortierung des Servers.
/// </summary>
public sealed class RuntimeNodeEventDto
{
    public required Guid Id { get; init; }
    public required string FlowNodeId { get; init; }
    public required FlowNodeStateDto State { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

/// <summary>Darstellungsstatus eines Knotens; keine vermeintliche lineare Fortschrittszahl.</summary>
public enum RuntimeNodeStatusDto
{
    Active,
    Completed,
    Cancelled,
    Failed
}
