using BPMN.Foundation;

namespace BPMN.Data;

public abstract record InputOutputSpecification : BaseElement
{
    public List<InputSet> InputSets { get; init; } = [];
    public List<OutputSet> OutputSets { get; init; } = [];
    public List<DataInput> DataInputs { get; init; } = [];
    public List<DataOutput> DataOutputs { get; init; } = [];
}