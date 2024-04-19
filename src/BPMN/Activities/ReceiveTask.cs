using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Service;

namespace BPMN.Activities;

public class ReceiveTask(string name, IFlowElementContainer container) : Task(name, container)
{
    [Required] public string Implementation { get; set; } = "";
    public bool Instantiate { get; set; }
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}