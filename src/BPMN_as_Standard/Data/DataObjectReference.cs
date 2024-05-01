namespace BPMN.Data;

public record DataObjectReference : ItemAwareElement
{
    public required DataObject DataObjectRef { get; init; }
}