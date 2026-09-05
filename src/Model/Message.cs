using System.Text.Json.Serialization;

namespace Model;

public record Message
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
    
    [JsonConverter(typeof(JsonStringConverter))]
    public string? Variables { get; init; }
    public int TimeToLive { get; init; } = 3600;
    public Guid? InstanceId { get; set; }
}