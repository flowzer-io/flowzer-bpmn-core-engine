namespace BPMN.Process;

public class GlobalScriptTask : GlobalTask
{
    public string? ScriptLanguage { get; set; }
    public required string Script { get; set; }
}