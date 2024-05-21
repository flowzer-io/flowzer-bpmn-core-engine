namespace BPMN.Process;

public record LaneSet
{
    public required string Name { get; init; }
    
    public FlowzerList<Lane> Lanes { get; init; } = [];
    public Lane? ParentLane { get; init; }
}