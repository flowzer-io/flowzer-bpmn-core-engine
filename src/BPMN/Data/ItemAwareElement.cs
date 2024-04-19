using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Data;

public class ItemAwareElement : BaseElement
{
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}