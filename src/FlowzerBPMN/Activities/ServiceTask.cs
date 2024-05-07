using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record ServiceTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }
    
    public int FlowzerRetries { get; init; }
    public List<FlowzerIoMapping>? InputMappings { get; init; }
    public List<FlowzerIoMapping>? OutputMappings { get; init; }
}