using BPMN.Artifacts;

namespace BPMN.Common;

public abstract record FlowElement : BaseElement
{
    public required string Name { get; init; }
    public Auditing? Auditing { get; init; }
    public Monitoring? Monitoring { get; init; }
    public FlowzerList<CategoryValue>? CategoryValueRefs { get; init; }
}