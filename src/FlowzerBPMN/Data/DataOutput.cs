namespace BPMN.Data;

public record DataOutput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public FlowzerList<OutputSet>? OutputSetRefs { get; init; }
    public FlowzerList<OutputSet>? OutputSetWithWhileExecuting { get; init; }
    public FlowzerList<OutputSet>? OutputSetWithOptional { get; init; }
}