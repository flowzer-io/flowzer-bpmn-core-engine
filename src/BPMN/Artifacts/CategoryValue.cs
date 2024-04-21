using BPMN.Activities;
using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Artifacts;

public class CategoryValue : BaseElement
{
    public required string Value { get; set; }
    public List<FlowElement> CategorizedFlowElements { get; set; } = [];
}