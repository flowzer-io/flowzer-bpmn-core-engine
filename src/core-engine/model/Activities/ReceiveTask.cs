using System.ComponentModel.DataAnnotations;
using Common;
using Service;

namespace Activities;

public class ReceiveTask : Task
{
    [Required] public string Implementation { get; set; } = "";
    public bool Instantiate { get; set; }
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}