namespace BPMN.Flowzer.Events;

public record FlowzerBoundaryMessageEvent : BoundaryEvent
{
    public required MessageDefinition MessageDefinition { get; init; }
}