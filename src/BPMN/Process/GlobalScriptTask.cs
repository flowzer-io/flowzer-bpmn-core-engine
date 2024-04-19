namespace BPMN.Process;

public class GlobalScriptTask(string name, string script) : GlobalTask(name)
{
    public string? ScriptLanguage { get; set; }
    public string Script { get; set; } = script;
}