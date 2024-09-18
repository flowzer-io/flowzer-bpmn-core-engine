using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

public class MessageDto
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
    
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Variables { get; init; }
    public int TimeToLive { get; init; } = 3600;

    public Guid? InstanceId { get; set; }
    
    
}