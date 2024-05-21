using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Flowzer;
using BPMN.Service;

namespace BPMN.Activities;

public record ReceiveTask : Task, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public bool Instantiate { get; init; }
    
    public Message? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}