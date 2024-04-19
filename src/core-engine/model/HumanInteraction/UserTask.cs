using System.ComponentModel.DataAnnotations;
using Common;

namespace HumanInteraction;

public class UserTask(string name, IFlowElementContainer container) : Activities.Task(name, container)
{
    [Required] public string Implementation { get; set; } = "";
    
    public List<Rendering> Renderings { get; set; } = [];
}