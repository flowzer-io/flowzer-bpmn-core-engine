namespace BPMN.Activities;

/// <summary>
/// Eine Aufgabe, die eine Nachricht aussendet. Ohne <see cref="Implementation"/> korreliert
/// die Engine sie selbst und läuft sofort weiter; mit Auftragstyp wartet sie wie bei einem
/// Service-Task auf den externen Worker.
/// </summary>
public record SendTask : Task, IFlowzerInputMapping, IFlowzerWorkerTask
{ 
    public string Implementation { get; init; } = "";

    public int FlowzerRetries { get; init; }

    public MessageDefinition? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
}
