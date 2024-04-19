using BPMN.Artifacts;
using BPMN.Foundation;
using BPMN.Process;

namespace BPMN.Common;

public abstract class FlowElement(string name, IFlowElementContainer container) : BaseElement
{
    public string Name { get; set; } = name;
    public IFlowElementContainer Container { get; set; } = container;
    public Auditing? Auditing { get; set; }
    public Monitoring? Monitoring { get; set; }
    public List<CategoryValue> CategoryValueRefs { get; set; } = [];
}