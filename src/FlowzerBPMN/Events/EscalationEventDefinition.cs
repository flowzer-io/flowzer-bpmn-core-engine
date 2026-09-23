namespace BPMN.Events;

public record EscalationEventDefinition
{
    public Escalation? EscalationRef { get; init; }
}
