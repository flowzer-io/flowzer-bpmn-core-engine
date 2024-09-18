namespace WebApiEngine.Shared;

public class MessageSubscriptionDto
{
    public required MessageDefinitionDto Message { get; set; }
    public required string ProcessId { get; set; }
    public required string RelatedDefinitionId { get; set; }
    public required Guid DefinitionId { get; set; }
    public Guid? ProcessInstanceId { get; set; }  
}

