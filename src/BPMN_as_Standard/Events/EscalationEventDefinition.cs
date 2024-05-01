namespace BPMN.Events;

public record EscalationEventDefinition
{
    public Escalation? EscalationRed { get; init; }
}