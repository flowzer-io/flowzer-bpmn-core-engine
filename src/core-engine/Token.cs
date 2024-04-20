namespace core_engine;

/// <summary>
/// The token indicates the current position of the process instance's execution.
/// </summary>
public class Token
{
    /// <summary>
    /// The unique ID of the token.
    /// </summary>
    public required Guid Id { get; set; }
    
    /// <summary>
    /// The time at which the token reached the node.
    /// </summary>
    public required DateTime Time { get; set; }
    
    /// <summary>
    /// The ID of the node where the token is currently located.
    /// </summary>
    public required string BpmnNodeId { get; set; }
}