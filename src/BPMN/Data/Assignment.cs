using BPMN.Common;

namespace BPMN.Data;

public class Assignment(Expression from, Expression to)
{
    public Expression From { get; set; } = from;
    public Expression To { get; set; } = to;
}