using BPMN.Common;
using BPMN.Flowzer;
using BPMN.Service;

namespace BPMN.Activities;

public record SendTask : Task, IFlowzerInputMapping
{ 
    public required string Implementation { get; init; }
    
    public Message? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
    public FlowzerIoMapping? InputMapping { get; init; }
}