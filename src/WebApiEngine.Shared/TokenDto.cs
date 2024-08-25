namespace WebApiEngine.Shared;

public class TokenDto
{
    public Guid Id { get; set; }
    public FlowNodeStateDto State { get; set; }
    public string? CurrentFlowNodeId { get; set; }
}