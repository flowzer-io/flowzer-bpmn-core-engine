namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageThrowEvent : IntermediateThrowEvent
{
    public required string Implementation { get; init; }
}