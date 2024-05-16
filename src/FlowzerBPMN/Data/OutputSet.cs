namespace BPMN.Data;

public record OutputSet
{
    public required string Name { get; init; }

    public ImmutableList<DataOutput>? DataOutputRefs { get; init; }
    public ImmutableList<DataOutput>? OptionalOutputRefs { get; init; }
    public ImmutableList<DataOutput>? WhileExecutingOutputRefs { get; init; }
    public ImmutableList<InputSet>? InputSetRefs { get; init; }
}