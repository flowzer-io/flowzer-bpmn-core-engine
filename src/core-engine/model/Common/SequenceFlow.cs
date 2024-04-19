namespace Common;

public class SequenceFlow(FlowNode sourceRef, FlowNode targetRef) : FlowElement
{
    public bool IsImmediate { get; set; }
    public FlowNode SourceRef { get; set; } = sourceRef;
    public FlowNode TargetRef { get; set; } = targetRef;
    public Expression? ConditionExpression { get; set; }
}