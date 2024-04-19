using Common;

namespace Data;

public class DataObject(string name, IFlowElementContainer container) : FlowElement(name, container), IItemAwareElement
{
    public bool IsCollection { get; set; }
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}