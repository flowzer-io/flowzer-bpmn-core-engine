namespace core_engine;

public record MessageDefinition
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
}