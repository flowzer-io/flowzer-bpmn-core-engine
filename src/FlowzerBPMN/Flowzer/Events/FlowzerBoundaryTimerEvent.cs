namespace BPMN.Flowzer.Events;

public record FlowzerBoundaryTimerEvent : BoundaryEvent, IFlowzerTimerEvent
{
    public FlowzerTimerType? TimerType => FlowzerTimerTypeResolver.GetTimerType(TimerDefinition);

    public required TimerEventDefinition TimerDefinition { get; init; }
}
