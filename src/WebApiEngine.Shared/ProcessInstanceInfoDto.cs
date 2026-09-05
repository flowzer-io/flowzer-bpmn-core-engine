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
    
    public ProcessInstanceStateDto State { get; set; }
    public List<TokenDto> Tokens { get; set; } = [];

    /// <summary>
    /// Startzeitpunkt der Instanz (UTC), abgeleitet aus dem ältesten Token.
    /// Null, solange die Instanz noch kein Token besitzt.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Endzeitpunkt der Instanz (UTC). Nur gesetzt, wenn die Instanz beendet ist.
    /// </summary>
    public DateTime? FinishedAt { get; set; }
}
