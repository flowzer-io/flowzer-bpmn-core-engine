using System.ComponentModel.DataAnnotations;
using BPMN.Common;

namespace BPMN.Activities;

public class BusinessRuleTask(string name, IFlowElementContainer container) : Task(name, container)
{
    [Required] public string Implementation { get; set; } = "";
}