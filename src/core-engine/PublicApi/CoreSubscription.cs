namespace core_engine;

/// <summary>
/// Beschreibt einen initialen Einstiegspunkt in eine geladene Definition.
/// </summary>
public class CoreSubscription
{
    public required CoreSubscriptionType Type { get; init; }

    public required string BpmnNodeId { get; init; }

    public required string Name { get; init; }
}

public enum CoreSubscriptionType
{
    Start,
    Message,
    Signal
}
