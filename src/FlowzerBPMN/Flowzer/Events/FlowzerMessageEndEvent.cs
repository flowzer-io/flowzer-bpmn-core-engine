namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Endereignis, das eine Nachricht aussendet und danach seinen Pfad beendet. Die beiden
/// Ausführungsarten entsprechen denen von <see cref="FlowzerIntermediateMessageThrowEvent"/>.
/// </summary>
public record FlowzerMessageEndEvent : EndEvent, IFlowzerWorkerTask
{
    public string Implementation { get; init; } = "";

    public int FlowzerRetries { get; init; }

    /// <summary>Die ausgesendete Nachricht. <c>null</c>, wenn nur ein Worker-Auftrag vergeben wird.</summary>
    public MessageDefinition? MessageDefinition { get; init; }
}
