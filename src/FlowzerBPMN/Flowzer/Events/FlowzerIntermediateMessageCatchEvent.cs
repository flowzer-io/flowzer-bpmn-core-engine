namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageCatchEvent : IntermediateThrowEvent
{
    public required Message Message { get; init; }
}