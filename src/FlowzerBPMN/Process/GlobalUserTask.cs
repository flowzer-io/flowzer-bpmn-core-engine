using BPMN.HumanInteraction;

namespace BPMN.Process;

public record GlobalUserTask : GlobalTask
{
    public required string Implementation { get; init; }

    public ImmutableList<Rendering> Renderings { get; init; } = [];
}