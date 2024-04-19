using BPMN.Common;

namespace BPMN.Events;

public class ConditionalEventDefinition : EventDefinition
{
    public Expression? Condition { get; set; }
}