namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Startereignis mit <c>escalationEventDefinition</c>. Es steht ausschliesslich in einem
/// Event-Subprozess und faengt eine Eskalation seines umschliessenden Scopes — mit dem Code des
/// referenzierten <c>bpmn:escalation</c>, ohne <c>escalationRef</c> jede Eskalation.
/// </summary>
public record FlowzerEscalationStartEvent : StartEvent
{
    public Escalation? Escalation { get; init; }
}
