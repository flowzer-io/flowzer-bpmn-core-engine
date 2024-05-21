namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateSignalThrowEvent : IntermediateThrowEvent
{
    public required Signal Signal { get; init; }
}