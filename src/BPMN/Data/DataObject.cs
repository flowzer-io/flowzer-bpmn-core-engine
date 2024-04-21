using BPMN.Common;

namespace BPMN.Data;

public class DataObject : FlowElement, IItemAwareElement
{
    public bool IsCollection { get; set; }
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}