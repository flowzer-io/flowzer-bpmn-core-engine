using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    [Required] public string Implementation { get; init; } = "";
    public FlowzerIoMapping? InputMapping { get; init; }
    public FlowzerIoMapping? OutputMapping { get; init; }
}