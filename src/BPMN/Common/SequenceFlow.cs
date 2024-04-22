namespace BPMN.Common;

public class SequenceFlow : FlowElement
{
    public bool IsImmediate { get; set; }
    public required CatchEvent SourceRef { get; set; }
    public required CatchEvent TargetRef { get; set; }
    public Expression? ConditionExpression { get; set; }
}