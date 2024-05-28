using System.Text.Json;

namespace Model;

public record Message
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
    public object? Variables { get; init; }
    public int TimeToLive { get; init; } = 3600;
}