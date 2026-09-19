namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Boundary-Event mit <c>errorEventDefinition</c>. Es fängt einen BPMN-Fehler mit dem Code
/// des referenzierten <c>bpmn:error</c>; ohne <c>errorRef</c> fängt es jeden Fehler. Das Fangen
/// ist laut BPMN-Spezifikation immer unterbrechend.
/// </summary>
public record FlowzerBoundaryErrorEvent : BoundaryEvent
{
    public Error? Error { get; init; }
}
