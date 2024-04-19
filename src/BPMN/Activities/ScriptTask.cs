using System.ComponentModel.DataAnnotations;
using BPMN.Common;

namespace BPMN.Activities;

public class ScriptTask(string name, IFlowElementContainer container) : Task(name, container)
{
    [Required] public string ScriptFormat { get; set; } = "";
    [Required] public string Script { get; set; } = "";
}