namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Zwischenereignis, das eine Nachricht aussendet.
///
/// Ohne <see cref="Implementation"/> korreliert die Engine die Nachricht selbst: Sie stellt
/// sie mit Name, ausgewertetem Korrelationsschlüssel und den gemappten Eingabewerten bereit,
/// und der Prozess läuft sofort weiter. Mit Auftragstyp verhält sich das Ereignis wie ein
/// Service-Task und wartet auf den Worker.
/// </summary>
public record FlowzerIntermediateMessageThrowEvent : IntermediateThrowEvent, IFlowzerWorkerTask
{
    public string Implementation { get; init; } = "";

    public int FlowzerRetries { get; init; }

    /// <summary>Die ausgesendete Nachricht. <c>null</c>, wenn nur ein Worker-Auftrag vergeben wird.</summary>
    public MessageDefinition? MessageDefinition { get; init; }
}
