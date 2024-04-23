namespace BPMN.Process;

public record GlobalScriptTask : GlobalTask
{
    public string? ScriptLanguage { get; init; }
    public required string Script { get; init; }
}