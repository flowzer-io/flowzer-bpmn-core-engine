using System.ComponentModel.DataAnnotations;

namespace BPMN.Process;

public class LaneSet
{
    [Required] public string Name { get; set; } = "";
    
    public List<Lane> Lanes { get; set; } = [];
    public Lane? ParentLane { get; set; }
}