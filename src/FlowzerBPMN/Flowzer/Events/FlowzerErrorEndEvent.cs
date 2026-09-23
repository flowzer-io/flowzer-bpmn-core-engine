namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Ende-Ereignis mit <c>errorEventDefinition</c>. Erreicht ein Token dieses Ereignis,
/// löst die Engine einen BPMN-Fehler mit dem Code des referenzierten <c>bpmn:error</c> aus.
/// Ohne <c>errorRef</c> bleibt <see cref="Error"/> null; der Fehler trägt dann keinen Code.
/// </summary>
public record FlowzerErrorEndEvent : EndEvent
{
    public Error? Error { get; init; }
}
