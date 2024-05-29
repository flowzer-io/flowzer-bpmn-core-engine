namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageThrowEvent : IntermediateThrowEvent
{
    public required MessageDefinition MessageDefinition { get; init; }
}