namespace BPMN.HumanInteraction;

public record UserTask : Activities.Task
{
    public required string Implementation { get; init; }
    
    public List<Rendering> Renderings { get; init; } = [];
}