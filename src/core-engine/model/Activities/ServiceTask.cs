using System.ComponentModel.DataAnnotations;
using Common;

namespace Activities;

public class ServiceTask(string name, IFlowElementContainer container) : Task(name, container)
{
    [Required] public string Implementation { get; set; } = "";
}