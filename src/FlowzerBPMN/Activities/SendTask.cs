using BPMN.Common;
using BPMN.Service;

namespace BPMN.Activities;

public record SendTask : Task
{ 
    public required string Implementation { get; init; }
    
    public Message? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
}