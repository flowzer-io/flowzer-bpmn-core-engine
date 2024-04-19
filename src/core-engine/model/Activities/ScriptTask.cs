using System.ComponentModel.DataAnnotations;

namespace Activities;

public class ScriptTask : Task
{
    [Required] public string ScriptFormat { get; set; } = "";
    [Required] public string Script { get; set; } = "";
}