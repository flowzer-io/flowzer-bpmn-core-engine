namespace core_engine;

/// <summary>
/// Beschreibt eine aktuell wartende Interaktion der Engine.
/// </summary>
public class CoreInteraction
{
    public required CoreInteractionType Type { get; init; }

    public required string NodeId { get; init; }

    public required string Name { get; init; }

    public string? Implementation { get; init; }
}

public enum CoreInteractionType
{
    UserTask,
    ServiceTask
}
