namespace BPMN.Flowzer.Events;

public record FlowzerSignalEndEvent : EndEvent
{
    public required Signal Signal { get; init; }
}