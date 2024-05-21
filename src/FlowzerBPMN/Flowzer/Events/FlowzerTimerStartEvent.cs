namespace BPMN.Flowzer.Events;

public record FlowzerTimerStartEvent : StartEvent, IFlowzerTimerEvent
{
    public FlowzerTimerType? TimerType => FlowzerTimerTypeResolver.GetTimerType(TimerDefinition);

    public required TimerEventDefinition TimerDefinition { get; init; }
}