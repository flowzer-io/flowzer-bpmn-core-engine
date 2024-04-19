using Common;

namespace Data;

public class DataObject : FlowElement, IItemAwareElement
{
    public bool IsCollection { get; set; }
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}