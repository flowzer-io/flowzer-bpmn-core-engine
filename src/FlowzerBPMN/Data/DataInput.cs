namespace BPMN.Data;

public record DataInput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public FlowzerList<InputSet>? InputSetRefs { get; init; }
    public FlowzerList<InputSet>? InputSetWithWhileExecuting { get; init; }
    public FlowzerList<InputSet>? InputSetWithOptional { get; init; }
}