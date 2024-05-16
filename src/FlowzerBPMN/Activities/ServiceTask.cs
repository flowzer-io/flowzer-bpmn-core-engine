using BPMN.Flowzer;

namespace BPMN.Activities;

public record ServiceTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }
    
    public int FlowzerRetries { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}