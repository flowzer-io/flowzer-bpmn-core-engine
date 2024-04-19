using System.ComponentModel.DataAnnotations;
using Common;
using Foundation;

namespace Data;

public class Property
{
    [Required] public string Name { get; set; } = "";
}

public class DataInput
{
    [Required] public string Name { get; set; } = "";
    public bool IsCollection { get; set; }
    
    public List<InputSet> InputSetRefs { get; set; } = [];
}

public class DataOutput
{
    [Required] public string Name { get; set; } = "";
    public bool IsCollection { get; set; }
    
    public List<OutputSet> OutputSetRefs { get; set; } = [];
}

public class InputSet(string name)
{
    public string Name { get; set; } = name;

    public List<DataInput> DataInputRefs { get; set; } = [];
}

public class OutputSet(string name)
{
    public string Name { get; set; } = name;

    public List<DataOutput> DataOutputRefs { get; set; } = [];
}

public abstract class InputOutputSpecification : BaseElement
{
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataOutput> DataOutputs { get; set; } = [];
}
public class DataInputAssociation;
public class DataOutputAssociation;
public class DataStoreReference : FlowElement;

public class DataObject : FlowElement
{
    public bool IsCollection { get; set; }
}