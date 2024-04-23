using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Service;

namespace BPMN.Activities;

public record ReceiveTask : Task
{
    [Required] public string Implementation { get; init; } = "";
    public bool Instantiate { get; init; }
    
    public Message? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
}