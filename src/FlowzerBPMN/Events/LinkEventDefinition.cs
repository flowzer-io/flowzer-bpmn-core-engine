namespace BPMN.Events;

public record LinkEventDefinition : EventDefinition
{
    public string? Name { get; init; }
    public FlowzerList<LinkEventDefinition> Sources { get; init; } = [];
    public LinkEventDefinition? Target { get; init; }
}