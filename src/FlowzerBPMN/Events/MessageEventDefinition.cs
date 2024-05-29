namespace BPMN.Events;

public record MessageEventDefinition : EventDefinition
{
    public Operation? OperationRef { get; init; }
    public MessageDefinition? MessageRef { get; init; }
}