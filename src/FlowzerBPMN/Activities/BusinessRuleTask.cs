using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public List<FlowzerIoMapping>? InputMappings { get; init; }
    public List<FlowzerIoMapping>? OutputMappings { get; init; }
}