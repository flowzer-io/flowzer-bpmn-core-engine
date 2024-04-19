using System.ComponentModel.DataAnnotations;
using Common;
using Foundation;
using Service;

namespace Data;

public class Property : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
}

public class DataInput : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsCollection { get; set; }
    
    public List<InputSet> InputSetRefs { get; set; } = [];
    public List<InputSet> InputSetWithWhileExecuting { get; set; } = [];
    public List<InputSet> InputSetWithOptional { get; set; } = [];
}

public class DataOutput : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsCollection { get; set; }
    
    public List<OutputSet> OutputSetRefs { get; set; } = [];
    public List<OutputSet> OutputSetWithWhileExecuting { get; set; } = [];
    public List<OutputSet> OutputSetWithOptional { get; set; } = [];
}

public class InputSet(string name) : BaseElement
{
    public string Name { get; set; } = name;

    public List<DataInput> DataInputRefs { get; set; } = [];
    public List<DataInput> OptionalInputRefs { get; set; } = [];
    public List<DataInput> WhileExecutingInputRefs { get; set; } = [];
    public List<OutputSet> OutputSetRefs { get; set; } = [];
}

public class OutputSet(string name)
{
    public string Name { get; set; } = name;

    public List<DataOutput> DataOutputRefs { get; set; } = [];
    public List<DataOutput> OptionalOutputRefs { get; set; } = [];
    public List<DataOutput> WhileExecutingOutputRefs { get; set; } = [];
    public List<InputSet> InputSetRefs { get; set; } = [];
}

public abstract class InputOutputSpecification : BaseElement
{
    public List<InputSet> InputSets { get; set; } = [];
    public List<OutputSet> OutputSets { get; set; } = [];
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataOutput> DataOutputs { get; set; } = [];
}

public class InputOutputBinding(Operation operationRef, InputSet inputDataRef, OutputSet outputDataRef)
{
    public Operation OperationRef { get; set; } = operationRef;
    public InputSet InputDataRef { get; set; } = inputDataRef;
    public OutputSet OutputDataRef { get; set; } = outputDataRef;
}
public class DataInputAssociation(ItemAwareElement targetRef) : DataAssociation(targetRef);
public class DataOutputAssociation(ItemAwareElement targetRef) : DataAssociation(targetRef);

public class DataStoreReference : ItemAwareElement
{
    public DataStore? DataStoreRef { get; set; }
}

public class DataStore(string name) : ItemAwareElement, IRootElement
{
    public string Name { get; set; } = name;
    public bool IsUnlimited { get; set; }
    public int? Capacity { get; set; }
}

public class DataObject : FlowElement, IItemAwareElement
{
    public bool IsCollection { get; set; }
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}

public class DataObjectReference(DataObject dataObjectRef) : ItemAwareElement
{
    public DataObject DataObjectRef { get; set; } = dataObjectRef;
}

public interface IItemAwareElement : IBaseElement
{
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}

public class ItemAwareElement : BaseElement
{
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}

public class DataState : BaseElement
{
    public string Name { get; set; } = "";
}

public class DataAssociation(ItemAwareElement targetRef) : BaseElement
{
    public List<Assignment> Assignments { get; set; } = [];
    public FormalExpression? Transformation { get; set; }
    public ItemAwareElement TargetRef { get; set; } = targetRef;
    public ItemAwareElement? SourceRef { get; set; }
}

public class Assignment(Expression from, Expression to)
{
    public Expression From { get; set; } = from;
    public Expression To { get; set; } = to;
}