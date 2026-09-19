namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Startereignis mit <c>errorEventDefinition</c>. Es steht ausschliesslich in einem
/// Event-Subprozess und faengt einen BPMN-Fehler seines umschliessenden Scopes — mit dem Code des
/// referenzierten <c>bpmn:error</c>, ohne <c>errorRef</c> jeden Fehler.
///
/// Ein Fehler-Start ist laut BPMN 2.0 immer unterbrechend: Der Scope kann nach einem Fehler
/// nicht weiterlaufen.
/// </summary>
public record FlowzerErrorStartEvent : StartEvent
{
    public Error? Error { get; init; }
}
