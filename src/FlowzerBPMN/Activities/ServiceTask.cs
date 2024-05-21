using BPMN.Flowzer;

namespace BPMN.Activities;

public record ServiceTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }
    
    public int FlowzerRetries { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}