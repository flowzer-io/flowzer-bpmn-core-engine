namespace BPMN.Flowzer.Events;

public record FlowzerMessageEndEvent : EndEvent
{
    public required string Implementation { get; init; }
}