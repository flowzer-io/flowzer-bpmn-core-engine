using BPMN.Common;
using BPMN.Events;

namespace BPMN.Activities;

public class ComplexBehaviorDefinition
{
    public FormalExpression? Condition { get; set; }
    public ImplicitThrowEvent? Event { get; set; }
}