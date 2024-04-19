using Common;

namespace Events;

public class ConditionalEventDefinition : EventDefinition
{
    public Expression? Condition { get; set; }
}