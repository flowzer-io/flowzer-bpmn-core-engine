namespace BPMN.Data;

public record DataInput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public List<InputSet> InputSetRefs { get; init; } = [];
    public List<InputSet> InputSetWithWhileExecuting { get; init; } = [];
    public List<InputSet> InputSetWithOptional { get; init; } = [];
}