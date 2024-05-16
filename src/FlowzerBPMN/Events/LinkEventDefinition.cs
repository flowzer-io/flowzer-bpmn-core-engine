namespace BPMN.Events;

public record LinkEventDefinition : EventDefinition
{
    public string? Name { get; init; }
    public ImmutableList<LinkEventDefinition> Sources { get; init; } = [];
    public LinkEventDefinition? Target { get; init; }
}