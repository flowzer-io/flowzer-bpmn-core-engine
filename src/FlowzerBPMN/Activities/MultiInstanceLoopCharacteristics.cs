namespace BPMN.Activities;

public record MultiInstanceLoopCharacteristics : LoopCharacteristics
{
    public bool IsSequential { get; init; }
    public MultiInstanceBehavior Behavior { get; init; }
    
    public Expression? LoopCardinality { get; init; }
    public Expression? CompletionCondition { get; init; }
    public FlowzerList<ComplexBehaviorDefinition>? ComplexBehaviorDefinitions { get; init; }
    public EventDefinition? OneBehaviorEventRef { get; init; }
    public EventDefinition? NoneBehaviorEventRef { get; init; }
    public ItemAwareElement? LoopDataInputRef { get; init; }
    public ItemAwareElement? LoopDataOutputRef { get; init; }
    public DataOutput? OutputDataItem { get; init; }
    public DataInput? InputDataItem { get; init; }
}