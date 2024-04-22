using System.ComponentModel.DataAnnotations;
using BPMN.Common;

namespace BPMN.Activities;

public record BusinessRuleTask : Task
{
    [Required] public string Implementation { get; init; } = "";
}