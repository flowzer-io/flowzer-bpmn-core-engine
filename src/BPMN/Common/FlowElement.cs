using BPMN.Artifacts;
using BPMN.Foundation;
using BPMN.Process;

namespace BPMN.Common;

public abstract class FlowElement : BaseElement
{
    public required string Name { get; set; }
    public required IFlowElementContainer Container { get; set; }
    public Auditing? Auditing { get; set; }
    public Monitoring? Monitoring { get; set; }
    public List<CategoryValue> CategoryValueRefs { get; set; } = [];
}