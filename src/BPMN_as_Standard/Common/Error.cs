namespace BPMN.Common;

public record Error
{
    public required string Name { get; init; }
    public string? ErrorCode { get; init; }
    
    public ItemDefinition? StructureRef { get; init; }
}