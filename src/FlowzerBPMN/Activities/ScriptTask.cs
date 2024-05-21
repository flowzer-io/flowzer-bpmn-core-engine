using BPMN.Flowzer;

namespace BPMN.Activities;

public record ScriptTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string ScriptFormat { get; init; }
    public required string Script { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}