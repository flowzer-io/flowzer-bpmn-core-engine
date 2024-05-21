namespace BPMN.Events;

public record ErrorEventDefinition : EventDefinition
{
    public Error? Error { get; init; }
}