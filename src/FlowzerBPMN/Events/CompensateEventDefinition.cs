namespace BPMN.Events;

public record CompensateEventDefinition : EventDefinition
{
    public bool WaitForCompletion { get; init; }
    public Activity? ActivityRef { get; init; }
}