namespace BPMN.Events;

public record SignalEventDefinition : EventDefinition
{
    public Signal? SignalRef { get; init; }
}