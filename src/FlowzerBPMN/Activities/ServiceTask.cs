using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record ServiceTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }
    
    public int FlowzerRetries { get; init; }
    public FlowzerIoMapping? InputMapping { get; init; }
    public FlowzerIoMapping? OutputMapping { get; init; }
}