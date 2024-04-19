using System.ComponentModel.DataAnnotations;

namespace HumanInteraction;

public class UserTask : Activities.Task
{
    [Required] public string Implementation { get; set; } = "";
    
    public List<Rendering> Renderings { get; set; } = [];
}