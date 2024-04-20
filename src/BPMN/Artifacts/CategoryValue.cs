using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Artifacts;

public class CategoryValue(string value) : BaseElement
{
    public string Value { get; set; } = value;
    public List<FlowElement> CategorizedFlowElements { get; set; } = [];
}