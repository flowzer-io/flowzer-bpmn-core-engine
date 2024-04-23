using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Process;

public record Lane : BaseElement
{
    public required string Name { get; init; }
    
    public required LaneSet LaneSet { get; init; }
    public LaneSet? ChildLaneSet { get; init; }
    public List<FlowNode> FlowNodeRefs { get; init; } = [];
    public IBaseElement? PartitionElementRef { get; init; }
    public IBaseElement? PartitionElement { get; init; }
}