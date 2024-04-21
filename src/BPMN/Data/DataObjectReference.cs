namespace BPMN.Data;

public class DataObjectReference : ItemAwareElement
{
    public required DataObject DataObjectRef { get; set; }
}