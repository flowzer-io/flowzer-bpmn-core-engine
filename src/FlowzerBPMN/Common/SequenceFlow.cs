namespace BPMN.Common;

public record SequenceFlow : FlowElement
{
    public bool IsImmediate { get; init; }
    public required FlowNode SourceRef { get; init; }
    public required FlowNode TargetRef { get; init; }
    public Expression? ConditionExpression { get; init; }
    
    public string? FlowzerCondition { get; init; }
    public bool? FlowzerIsDefault { get; init; }
}