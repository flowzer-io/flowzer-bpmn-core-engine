using BPMN.HumanInteraction;

namespace BPMN.Process;

public class GlobalUserTask : GlobalTask
{
    public required string Implementation { get; set; }

    public List<Rendering> Renderings { get; set; } = [];
}