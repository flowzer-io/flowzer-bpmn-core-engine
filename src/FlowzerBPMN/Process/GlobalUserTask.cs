using BPMN.HumanInteraction;

namespace BPMN.Process;

public record GlobalUserTask : GlobalTask
{
    public required string Implementation { get; init; }

    public FlowzerList<Rendering> Renderings { get; init; } = [];
}