using System.ComponentModel.DataAnnotations;
using Process;

namespace HumanInteraction;

public abstract class HumanPerformer : Performer;
public abstract class PotentialOwner : HumanPerformer;
public class ManualTask : Activities.Task;

public class UserTask : Activities.Task
{
    [Required] public string Implementation { get; set; } = "";
    
    public List<Rendering> Renderings { get; set; } = [];
}

public abstract class Rendering;
