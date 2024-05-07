using BPMN.Common;
using BPMN.Events;

namespace BPMN.Flowzer;

public record FlowzerMessageStartEvent : StartEvent
{
    public required Message Message { get; init; }
}