using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Process;

public class Lane : BaseElement
{
    public required string Name { get; set; }
    
    public required LaneSet LaneSet { get; set; }
    public LaneSet? ChildLaneSet { get; set; }
    public List<FlowNode> FlowNodeRefs { get; set; } = [];
    public IBaseElement? PartitionElementRef { get; set; }
    public IBaseElement? PartitionElement { get; set; }
}