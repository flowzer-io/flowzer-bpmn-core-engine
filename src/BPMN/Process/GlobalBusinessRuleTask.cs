namespace BPMN.Process;

public record GlobalBusinessRuleTask : GlobalTask
{
    public required string Implementation { get; init; }
}