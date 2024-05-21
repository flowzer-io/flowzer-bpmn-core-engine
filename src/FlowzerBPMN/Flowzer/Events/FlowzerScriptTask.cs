namespace BPMN.Flowzer.Events;

public record FlowzerScriptTask : ScriptTask
{
    public FlowzerScriptTaskType Type { get; init; }
    public string? Implementation { get; init; }
    public string? ResultVar { get; init; }
}

public enum FlowzerScriptTaskType
{
    Script,
    Service
}