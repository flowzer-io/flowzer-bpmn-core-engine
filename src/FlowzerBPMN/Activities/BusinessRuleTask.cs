using System.ComponentModel.DataAnnotations;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}