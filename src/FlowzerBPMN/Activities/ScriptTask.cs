using BPMN.Flowzer;

namespace BPMN.Activities;

public record ScriptTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string ScriptFormat { get; init; }
    public required string Script { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}