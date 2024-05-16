namespace BPMN.Data;

public record DataInput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public ImmutableList<InputSet>? InputSetRefs { get; init; }
    public ImmutableList<InputSet>? InputSetWithWhileExecuting { get; init; }
    public ImmutableList<InputSet>? InputSetWithOptional { get; init; }
}