using BPMN.Common;

namespace BPMN.Events;

public record TimerEventDefinition : EventDefinition
{
    public Expression? TimeDate { get; init; }
    public Expression? TimeCycle { get; init; }
    public Expression? TimeDuration { get; init; }
}