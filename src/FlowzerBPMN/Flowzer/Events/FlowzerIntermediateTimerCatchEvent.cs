namespace BPMN.Flowzer.Events;

public record FlowzerIntermediateTimerCatchEvent : IntermediateCatchEvent, IFlowzerTimerEvent
{
    public FlowzerTimerType? TimerType => FlowzerTimerTypeResolver.GetTimerType(TimerDefinition);
    public required TimerEventDefinition TimerDefinition { get; init; }
}