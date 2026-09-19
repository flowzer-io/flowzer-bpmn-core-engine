namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Ende-Ereignis mit <c>escalationEventDefinition</c>. Es meldet eine Eskalation nach aussen
/// und beendet danach seinen eigenen Pfad wie ein gewoehnliches Ende.
/// </summary>
public record FlowzerEscalationEndEvent : EndEvent
{
    public Escalation? Escalation { get; init; }
}
