using BPMN.Foundation;

namespace BPMN.Data;

public abstract record InputOutputSpecification : BaseElement
{
    public ImmutableList<InputSet>? InputSets { get; init; }
    public ImmutableList<OutputSet>? OutputSets { get; init; }
    public ImmutableList<DataInput>? DataInputs { get; init; }
    public ImmutableList<DataOutput>? DataOutputs { get; init; }
}