namespace core_engine;

public class InteractionResult
{
    /// <summary>
    /// The activity that the result of the interaction pertains to.
    /// </summary>
    public required string FlowNodeId { get; set; }
    
    /// <summary>
    /// The ID of the token that triggered this activity. (This is necessary to clarify which of the
    /// tokens at the activity should be continued. This is important for distributed systems.)
    /// </summary>
    public required Guid TokenId { get; set; }

    /// <summary>
    /// The data resulting from the interaction.
    /// </summary>
    public required Dictionary<string, object> ResultData { get; set; }
    
}