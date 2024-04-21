namespace BPMN.Activities;

public class ScriptTask : Task
{
    public required string ScriptFormat { get; set; }
    public required string Script { get; set; }
}