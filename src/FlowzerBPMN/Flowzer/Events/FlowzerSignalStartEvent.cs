using BPMN.Events;

namespace BPMN.Flowzer.Events;

public record FlowzerSignalStartEvent : StartEvent
{
    public required Signal Signal { get; init; }
}