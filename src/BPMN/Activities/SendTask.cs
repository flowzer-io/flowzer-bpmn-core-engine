using BPMN.Common;
using BPMN.Service;

namespace BPMN.Activities;

public class SendTask : Task
{ 
    public required string Implementation { get; set; }
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}