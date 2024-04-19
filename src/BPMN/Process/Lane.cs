using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Process;

public class Lane(LaneSet laneSet) : BaseElement
{
    [Required] public string Name { get; set; } = "";
    
    public LaneSet LaneSet { get; set; } = laneSet;
    public LaneSet? ChildLaneSet { get; set; }
    public List<FlowNode> FlowNodeRefs { get; set; } = [];
    public IBaseElement? PartitionElementRef { get; set; }
    public IBaseElement? PartitionElement { get; set; }
}