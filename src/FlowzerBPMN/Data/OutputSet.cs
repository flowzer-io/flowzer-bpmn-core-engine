namespace BPMN.Data;

public record OutputSet
{
    public required string Name { get; init; }

    public FlowzerList<DataOutput>? DataOutputRefs { get; init; }
    public FlowzerList<DataOutput>? OptionalOutputRefs { get; init; }
    public FlowzerList<DataOutput>? WhileExecutingOutputRefs { get; init; }
    public FlowzerList<InputSet>? InputSetRefs { get; init; }
}