namespace Model;

public class UserTaskResult
{
    public required string FlowNodeId { get; set; }
    public Guid TokenId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    
    public Variables? Data { get; set; }
}