namespace core_engine;

public abstract class InteractionRequest
{
    /// <summary>
    /// The ID of the service that should handle this event, e.g., Chrono/Webhook etc.
    /// </summary>
    public required string ServiceId { get; set; }
    
    /// <summary>
    /// The BPMN node to which data should be passed in the event of a service event.
    /// </summary>
    public required string ActivityId { get; set; }
    
    /// <summary>
    /// The data that should be passed to the service.
    /// </summary>
    public required Dictionary<string, object> Data { get; set; }
    
}