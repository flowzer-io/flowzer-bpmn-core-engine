namespace BPMN.Process;

public class LaneSet
{
    public required string Name { get; set; }
    
    public List<Lane> Lanes { get; set; } = [];
    public Lane? ParentLane { get; set; }
}