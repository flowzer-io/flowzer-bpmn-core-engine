namespace BPMN.Activities;

public record ScriptTask : Task
{
    public required string ScriptFormat { get; init; }
    public required string Script { get; init; }
}