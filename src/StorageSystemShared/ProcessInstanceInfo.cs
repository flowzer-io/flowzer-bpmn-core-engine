namespace StorageSystem;

public class ProcessInstanceInfo
{
    public required Guid InstanceId  { get; set; }
    public required string metaDefinitionId  { get; set; }
    public required Guid DefinitionId  { get; set; }
    
    public required string ProcessId  { get; set; }
    public required List<Token> Tokens  { get; set; }
    public required bool IsFinished  { get; set; }
    
    public required ProcessInstanceState State { get; set; }
    public required int MessageSubscriptionCount { get; set; }
    public required int SignalSubscriptionCount { get; set; }
    public required int UserTaskSubscriptionCount { get; set; }
    public required int ServiceSubscriptionCount { get; set; }
}