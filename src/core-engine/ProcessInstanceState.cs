namespace core_engine;

public class ProcessInstanceState
{
    /// <summary>
    /// The ID of the instance.
    /// </summary>
    public required Guid InstanceId { get; set; }
    
    /// <summary>
    /// The global variables of the instance.
    /// </summary>
    public required Dictionary<string, object> Data { get; set; }
    
    /// <summary>
    /// The resulting tokens from this process step.
    /// </summary>
    public required Token[] Tokens { get; set; }

    /// <summary>
    /// The interactions that are possible in this process step.
    /// </summary>
    public required InteractionRequest[] PossibleInteractions { get; set; }
    
    /// <summary>
    /// The interactions (via services) that are currently active and need to be subscribed to.
    /// </summary>
    public required InteractionRequest[] ServiceSubscriptions { get; set; }

}