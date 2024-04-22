using BPMN.Common;

namespace core_engine;

/// <summary>
/// The token indicates the current position of the process instance's execution.
/// </summary>
public class Token
{
    /// <summary>
    /// The unique ID of the token.
    /// </summary>
    public required Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// The ID of the node where the token is currently located.
    /// </summary>
    public required CatchEvent ActualNode { get; set; }
}