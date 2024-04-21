namespace BPMN.HumanInteraction;

public class UserTask : Activities.Task
{
    public required string Implementation { get; set; }
    
    public List<Rendering> Renderings { get; set; } = [];
}