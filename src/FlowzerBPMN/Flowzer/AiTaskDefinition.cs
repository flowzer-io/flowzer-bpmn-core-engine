namespace BPMN.Flowzer;

/// <summary>
/// Versionierter, nicht geheimer Vertrag einer Flowzer-KI-Aufgabe. Die Verbindung ist eine
/// stabile Referenz; Zugangsdaten bleiben ausschließlich im serverseitigen Secret-Store.
/// Ein- und Ausgaben liegen weiterhin in den normalen BPMN-I/O-Zuordnungen des Service-Tasks.
/// </summary>
public sealed record AiTaskDefinition(
    int ContractVersion,
    Guid ConnectionId,
    string? Model,
    int InstructionVersion,
    string Instruction,
    string ResultSchema,
    int MaxInputTokens,
    int MaxOutputTokens,
    int TimeoutSeconds);
