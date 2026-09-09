namespace Model;

/// <summary>
/// Nicht geheimer, unveraenderlicher Verbindungssnapshot eines deployten KI-Schritts.
/// Die eigentliche Anweisung und die Ein-/Ausgabevertraege bleiben Bestandteil des BPMN;
/// Zugangsdaten werden weiterhin erst serverseitig zur Laufzeit aufgeloest.
/// </summary>
public sealed record BoundAiTask(
    Guid ConnectionId,
    long ConnectionRevision,
    string Model);
