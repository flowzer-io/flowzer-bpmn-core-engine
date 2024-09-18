namespace WebApiEngine.Shared;

public class SignalSubscriptionDto
{
    public required string Signal { get; set; }
    public required string ProcessId { get; set; }

    public required string RelatedDefinitionId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required Guid? ProcessInstanceId { get; set; }
}