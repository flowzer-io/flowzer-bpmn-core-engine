using BPMN.Flowzer;

namespace BPMN.Activities;

public record ScriptTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string ScriptFormat { get; init; }
    public required string Script { get; init; }
    public FlowzerIoMapping? InputMapping { get; init; }
    public FlowzerIoMapping? OutputMapping { get; init; }
}