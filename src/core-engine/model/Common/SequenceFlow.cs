namespace Common;

public class SequenceFlow(string name, IFlowElementContainer container, FlowNode sourceRef, FlowNode targetRef) : FlowElement(name, container)
{
    public bool IsImmediate { get; set; }
    public FlowNode SourceRef { get; set; } = sourceRef;
    public FlowNode TargetRef { get; set; } = targetRef;
    public Expression? ConditionExpression { get; set; }
}