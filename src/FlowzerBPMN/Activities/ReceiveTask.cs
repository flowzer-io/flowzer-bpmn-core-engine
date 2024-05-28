using System.ComponentModel.DataAnnotations;

namespace BPMN.Activities;

public record ReceiveTask : Task, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public bool Instantiate { get; init; }
    
    public MessageDefinition? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}