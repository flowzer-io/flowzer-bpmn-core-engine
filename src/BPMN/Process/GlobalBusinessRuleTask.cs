namespace BPMN.Process;

public class GlobalBusinessRuleTask(string name, string implementation) : GlobalTask(name)
{
    public string Implementation { get; set; } = implementation;
}