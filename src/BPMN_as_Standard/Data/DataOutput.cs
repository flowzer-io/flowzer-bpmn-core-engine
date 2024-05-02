namespace BPMN.Data;

public record DataOutput : ItemAwareElement
{
    public required string Name { get; init; }
    public bool IsCollection { get; init; }
    
    public List<OutputSet> OutputSetRefs { get; init; } = [];
    public List<OutputSet> OutputSetWithWhileExecuting { get; init; } = [];
    public List<OutputSet> OutputSetWithOptional { get; init; } = [];
}