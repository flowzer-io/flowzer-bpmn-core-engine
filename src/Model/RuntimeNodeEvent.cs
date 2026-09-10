namespace Model;

/// <summary>
/// Unveränderlicher, datensparsamer Fakt über einen relevanten Laufzeitzustand eines BPMN-Knotens.
/// Prozessvariablen, Formularwerte und Akteure sind absichtlich kein Teil dieses Vertrags. Die
/// Engine erzeugt die Kennungen und den UTC-Zeitpunkt serverseitig; <see cref="CorrelationId"/>
/// verbindet Statuswechsel derselben Ausführungsoperation, ohne einen Personenbezug zu speichern.
/// </summary>
public sealed class RuntimeNodeEvent
{
    /// <summary>Stabile Kennung für die idempotente append-only Persistenz.</summary>
    public required Guid Id { get; init; }
    public required Guid ProcessInstanceId { get; init; }
    /// <summary>Beim Start gebundene Definition, nicht die später möglicherweise neueste Version.</summary>
    public required Guid DefinitionId { get; init; }
    /// <summary>Technische Korrelation einzelner paralleler Ausführungspfade.</summary>
    public required Guid TokenId { get; init; }
    public required string FlowNodeId { get; init; }
    public required FlowNodeState State { get; init; }
    /// <summary>Opaque Kennung einer serverseitigen Ausführungsoperation.</summary>
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
