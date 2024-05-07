using BPMN.Flowzer;

namespace BPMN.Activities;

public record ScriptTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string ScriptFormat { get; init; }
    public required string Script { get; init; }
    public List<FlowzerIoMapping>? InputMappings { get; init; }
    public List<FlowzerIoMapping>? OutputMappings { get; init; }
}