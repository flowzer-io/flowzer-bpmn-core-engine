using BPMN.Events;

namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateSignalCatchEvent : IntermediateCatchEvent
{
    public required Signal Signal { get; init; }
}

public record FlowzerIntermediateSignalThrowEvent : IntermediateThrowEvent
{
    public required Signal Signal { get; init; }
}