namespace BPMN.Data;

public class DataObjectReference(DataObject dataObjectRef) : ItemAwareElement
{
    public DataObject DataObjectRef { get; set; } = dataObjectRef;
}