namespace BPMN.Activities;

public abstract record ResourceAssignmentExpression
{
    public Expression? Expression { get; init; }
}