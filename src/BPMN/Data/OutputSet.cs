namespace BPMN.Data;

public record OutputSet
{
    public required string Name { get; init; }

    public List<DataOutput> DataOutputRefs { get; init; } = [];
    public List<DataOutput> OptionalOutputRefs { get; init; } = [];
    public List<DataOutput> WhileExecutingOutputRefs { get; init; } = [];
    public List<InputSet> InputSetRefs { get; init; } = [];
}