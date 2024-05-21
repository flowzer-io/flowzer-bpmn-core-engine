using System.ComponentModel.DataAnnotations;

namespace BPMN.Activities;

public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}