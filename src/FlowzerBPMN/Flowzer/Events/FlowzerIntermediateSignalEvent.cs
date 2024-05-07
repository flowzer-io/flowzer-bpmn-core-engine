using BPMN.Events;

namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateSignalEvent : StartEvent
{
    public required Signal Signal { get; init; }
}