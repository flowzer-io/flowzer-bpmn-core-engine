namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateSignalCatchEvent : IntermediateCatchEvent
{
    public required Signal Signal { get; init; }
}