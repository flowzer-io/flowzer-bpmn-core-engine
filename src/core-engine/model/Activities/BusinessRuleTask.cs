using System.ComponentModel.DataAnnotations;

namespace Activities;

public class BusinessRuleTask : Task
{
    [Required] public string Implementation { get; set; } = "";
}