using BPMN.Foundation;

namespace BPMN.Data;

public record InputSet : BaseElement
{
    public required string Name { get; init; }

    public ImmutableList<DataInput>? DataInputRefs { get; init; }
    public ImmutableList<DataInput>? OptionalInputRefs { get; init; }
    public ImmutableList<DataInput>? WhileExecutingInputRefs { get; init; }
    public ImmutableList<OutputSet>? OutputSetRefs { get; init; }
}