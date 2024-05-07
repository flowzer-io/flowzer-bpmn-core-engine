using BPMN.Events;

namespace BPMN.Flowzer.Events;

public record FlowzerTimerStartEvent : StartEvent
{
    public FlowzerTimerType? TimerType
    {
        get
        {
            if (TimerDefinition.TimeDate != null)
            {
                return FlowzerTimerType.TimeDate;
            }
            else if (TimerDefinition.TimeCycle != null)
            {
                return FlowzerTimerType.TimeCycle;
            }
            else if (TimerDefinition.TimeDuration != null)
            {
                return FlowzerTimerType.TimeDuration;
            }

            return null;
        }
    }
    public required TimerEventDefinition TimerDefinition { get; set; }
};