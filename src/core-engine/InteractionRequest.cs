namespace core_engine;

public abstract class InteractionRequest
{
    public required string ServiceName { get; set; }
    public required string ActivityId { get; set; }
    public required Dictionary<string, object> Inputdata { get; set; }

}