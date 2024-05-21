using BPMN.Foundation;

namespace BPMN.Data;

public record InputSet : BaseElement
{
    public required string Name { get; init; }

    public FlowzerList<DataInput>? DataInputRefs { get; init; }
    public FlowzerList<DataInput>? OptionalInputRefs { get; init; }
    public FlowzerList<DataInput>? WhileExecutingInputRefs { get; init; }
    public FlowzerList<OutputSet>? OutputSetRefs { get; init; }
}