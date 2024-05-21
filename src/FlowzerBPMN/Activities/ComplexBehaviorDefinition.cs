namespace BPMN.Activities;

public record ComplexBehaviorDefinition
{
    public FormalExpression? Condition { get; init; }
    public ImplicitThrowEvent? Event { get; init; }
}