namespace BPMN.Events;

public record Signal : IRootElement
{
    public string Name { get; init; } = "";
    public ItemDefinition? StructureRef { get; init; }
    
    public string? FlowzerId { get; init; }
}