namespace BPMN.Data;

public record DataOutput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public ImmutableList<OutputSet>? OutputSetRefs { get; init; }
    public ImmutableList<OutputSet>? OutputSetWithWhileExecuting { get; init; }
    public ImmutableList<OutputSet>? OutputSetWithOptional { get; init; }
}