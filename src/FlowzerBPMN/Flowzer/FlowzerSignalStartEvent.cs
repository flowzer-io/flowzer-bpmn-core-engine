using BPMN.Events;

namespace BPMN.Flowzer;

public record FlowzerSignalStartEvent : StartEvent
{
    public required Signal Signal { get; init; }
}