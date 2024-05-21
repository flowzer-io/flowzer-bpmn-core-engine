namespace BPMN.Flowzer.Events;

public record FlowzerMessageStartEvent : StartEvent
{
    public required Message Message { get; init; }
}