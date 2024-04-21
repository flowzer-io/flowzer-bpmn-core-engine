using BPMN.Foundation;

namespace BPMN.Data;

public class InputSet : BaseElement
{
    public required string Name { get; set; }

    public List<DataInput> DataInputRefs { get; set; } = [];
    public List<DataInput> OptionalInputRefs { get; set; } = [];
    public List<DataInput> WhileExecutingInputRefs { get; set; } = [];
    public List<OutputSet> OutputSetRefs { get; set; } = [];
}