namespace BPMN.Data;

public record DataObject : FlowElement, IItemAwareElement
{
    public bool IsCollection { get; init; }
    public ItemDefinition? ItemSubjectRef { get; init; }
    public DataState? DataState { get; init; }
}