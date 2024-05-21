using BPMN.Foundation;

namespace BPMN.Data;

public abstract record InputOutputSpecification : BaseElement
{
    public FlowzerList<InputSet>? InputSets { get; init; }
    public FlowzerList<OutputSet>? OutputSets { get; init; }
    public FlowzerList<DataInput>? DataInputs { get; init; }
    public FlowzerList<DataOutput>? DataOutputs { get; init; }
}