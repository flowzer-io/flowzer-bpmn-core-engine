using System.ComponentModel.DataAnnotations;
using Common;
using Service;

namespace Activities;

public class SendTask : Task
{
    [Required] public string Implementation { get; set; } = "";
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}