using System.ComponentModel.DataAnnotations;
using BPMN.Common;

namespace BPMN.Activities;

public class BusinessRuleTask : Task
{
    [Required] public string Implementation { get; set; } = "";
}