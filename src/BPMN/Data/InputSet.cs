using BPMN.Foundation;

namespace BPMN.Data;

public record InputSet : BaseElement
{
    public required string Name { get; init; }

    public List<DataInput> DataInputRefs { get; init; } = [];
    public List<DataInput> OptionalInputRefs { get; init; } = [];
    public List<DataInput> WhileExecutingInputRefs { get; init; } = [];
    public List<OutputSet> OutputSetRefs { get; init; } = [];
}