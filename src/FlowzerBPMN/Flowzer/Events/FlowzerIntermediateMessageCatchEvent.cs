namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageCatchEvent : IntermediateThrowEvent
{
    public required MessageDefinition MessageDefinition { get; init; }
}