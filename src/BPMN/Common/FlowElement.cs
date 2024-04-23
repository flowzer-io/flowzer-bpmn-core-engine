using BPMN.Artifacts;
using BPMN.Foundation;
using BPMN.Process;

namespace BPMN.Common;

public abstract record FlowElement : BaseElement
{
    public required string Name { get; init; }
    public required IFlowElementContainer Container { get; init; }
    public Auditing? Auditing { get; init; }
    public Monitoring? Monitoring { get; init; }
    public List<CategoryValue> CategoryValueRefs { get; init; } = [];
}