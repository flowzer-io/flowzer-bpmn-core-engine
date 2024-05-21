namespace BPMN.Events;

public record ConditionalEventDefinition : EventDefinition
{
    public Expression? Condition { get; init; }
}