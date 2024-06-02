namespace BPMN.Flowzer.Events;

public record FlowzerBoundarySignalEvent : BoundaryEvent
{
    public required Signal Signal { get; init; }
}