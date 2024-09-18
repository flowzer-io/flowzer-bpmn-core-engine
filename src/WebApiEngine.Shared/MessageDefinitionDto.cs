namespace WebApiEngine.Shared;

public class MessageDefinitionDto
{
    public required string Name { get; set; }
    public string? FlowzerId { get; set; }
    public string? FlowzerCorrelationKey { get; set; }
}