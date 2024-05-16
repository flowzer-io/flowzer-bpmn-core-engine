using System.ComponentModel.DataAnnotations;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}