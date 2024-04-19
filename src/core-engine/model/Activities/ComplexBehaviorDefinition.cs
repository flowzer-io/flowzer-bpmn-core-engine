using Common;
using Events;

namespace Activities;

public class ComplexBehaviorDefinition
{
    public FormalExpression? Condition { get; set; }
    public ImplicitThrowEvent? Event { get; set; }
}