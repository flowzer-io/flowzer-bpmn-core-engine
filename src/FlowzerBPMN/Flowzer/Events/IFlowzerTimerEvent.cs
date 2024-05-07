using BPMN.Events;

namespace BPMN.Flowzer.Events;

public interface IFlowzerTimerEvent
{
    public FlowzerTimerType? TimerType { get; }
    public TimerEventDefinition TimerDefinition { get; init; }
}

internal static class FlowzerTimerTypeResolver
{
    public static FlowzerTimerType? GetTimerType(TimerEventDefinition timerDefinition)
    {
        if (timerDefinition.TimeDate != null)
        {
            return FlowzerTimerType.TimeDate;
        }
        if (timerDefinition.TimeCycle != null)
        {
            return FlowzerTimerType.TimeCycle;
        }
        if (timerDefinition.TimeDuration != null)
        {
            return FlowzerTimerType.TimeDuration;
        }

        return null;
    }
}