namespace WebApiEngine.Shared;

public class TimerSubscriptionDto
{
    public Guid Id { get; set; }
    public DateTime DueAt { get; set; }
    public string FlowNodeId { get; set; } = string.Empty;
    public string ProcessId { get; set; } = string.Empty;
    public string RelatedDefinitionId { get; set; } = string.Empty;
    public Guid DefinitionId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public string Kind { get; set; } = string.Empty;
}
