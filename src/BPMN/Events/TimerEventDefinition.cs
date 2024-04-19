using BPMN.Common;

namespace BPMN.Events;

public class TimerEventDefinition : EventDefinition
{
    public Expression? TimeDate { get; set; }
    public Expression? TimeCycle { get; set; }
    public Expression? TimeDuration { get; set; }
}