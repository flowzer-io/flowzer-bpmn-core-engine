using System.ComponentModel.DataAnnotations;

namespace Activities;

public class ServiceTask : Task
{
    [Required] public string Implementation { get; set; } = "";
}