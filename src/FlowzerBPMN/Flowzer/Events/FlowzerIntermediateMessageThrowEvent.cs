namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageThrowEvent : IntermediateThrowEvent
{
    public required Message Message { get; init; }
}