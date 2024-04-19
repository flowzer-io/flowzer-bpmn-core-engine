using BPMN.Foundation;

namespace BPMN.Data;

public abstract class InputOutputSpecification : BaseElement
{
    public List<InputSet> InputSets { get; set; } = [];
    public List<OutputSet> OutputSets { get; set; } = [];
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataOutput> DataOutputs { get; set; } = [];
}