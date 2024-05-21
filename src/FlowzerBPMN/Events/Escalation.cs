namespace BPMN.Events;

public record Escalation
{
    public string EscalationCode { get; init; } = "";
    public string Name { get; init; } = "";
    
    public ItemDefinition? StructureRef { get; init; }
}