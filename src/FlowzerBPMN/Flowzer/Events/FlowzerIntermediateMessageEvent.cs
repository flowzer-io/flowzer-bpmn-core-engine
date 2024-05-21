using BPMN.Common;
using BPMN.Events;

namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateMessageCatchEvent : IntermediateThrowEvent
{
    public required Message Message { get; init; }
}
public record FlowzerIntermediateMessageThrowEvent : IntermediateThrowEvent
{
    public required Message Message { get; init; }
}