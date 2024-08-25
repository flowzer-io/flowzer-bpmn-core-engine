namespace WebApiEngine.Shared;

public class ProcessInstanceInfoDto
{
    public required Guid InstanceId { get; set; }
    
    public required Guid DefinitionId { get; set; }
    
    public required string RelatedDefinitionId { get; set; }
    public required string RelatedDefinitionName { get; set; }
    public int MessageSubscriptionCount { get; set; }
    public int SignalSubscriptionCount { get; set; }
    public int UserTaskSubscriptionCount { get; set; }
    public int ServiceSubscriptionCount { get; set; }
    
    public string State { get; set; }
    public List<TokenDto> Tokens { get; set; }
}