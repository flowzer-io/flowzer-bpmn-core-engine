using Foundation;

namespace Data;

public class InputSet(string name) : BaseElement
{
    public string Name { get; set; } = name;

    public List<DataInput> DataInputRefs { get; set; } = [];
    public List<DataInput> OptionalInputRefs { get; set; } = [];
    public List<DataInput> WhileExecutingInputRefs { get; set; } = [];
    public List<OutputSet> OutputSetRefs { get; set; } = [];
}