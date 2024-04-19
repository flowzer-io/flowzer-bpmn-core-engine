using System.ComponentModel.DataAnnotations;
using BPMN.Common;

namespace BPMN.HumanInteraction;

public class UserTask(string name, IFlowElementContainer container) : Activities.Task(name, container)
{
    [Required] public string Implementation { get; set; } = "";
    
    public List<Rendering> Renderings { get; set; } = [];
}