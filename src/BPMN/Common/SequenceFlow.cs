namespace BPMN.Common;

public class SequenceFlow : FlowElement
{
    public bool IsImmediate { get; set; }
    public required FlowNode SourceRef { get; set; }
    public required FlowNode TargetRef { get; set; }
    public Expression? ConditionExpression { get; set; }
}