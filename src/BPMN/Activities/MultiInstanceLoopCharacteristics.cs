using BPMN.Common;
using BPMN.Data;
using BPMN.Events;

namespace BPMN.Activities;

public class MultiInstanceLoopCharacteristics : LoopCharacteristics
{
    public bool IsSequential { get; set; }
    public MultiInstanceBehavior Behavior { get; set; }
    
    public Expression? LoopCardinality { get; set; }
    public Expression? CompletionCondition { get; set; }
    public List<ComplexBehaviorDefinition> ComplexBehaviorDefinitions { get; set; } = [];
    public EventDefinition? OneBehaviorEventRef { get; set; }
    public EventDefinition? NoneBehaviorEventRef { get; set; }
    public ItemAwareElement? LoopDataInputRef { get; set; }
    public ItemAwareElement? LoopDataOutputRef { get; set; }
    public DataOutput? OutputDataItem { get; set; }
    public DataInput? InputDataItem { get; set; }
}