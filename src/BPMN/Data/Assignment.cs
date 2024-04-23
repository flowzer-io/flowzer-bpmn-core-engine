using BPMN.Common;

namespace BPMN.Data;

public record Assignment(Expression from, Expression to)
{
    public Expression From { get; init; } = from;
    public Expression To { get; init; } = to;
}