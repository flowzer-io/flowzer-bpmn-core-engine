namespace BPMN.Flowzer.Events;

public record FlowzerMessageStartEvent : StartEvent
{
    public required MessageDefinition MessageDefinition { get; init; }
}