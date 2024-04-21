namespace BPMN.Common;

public class Error
{
    public required string Name { get; set; }
    public string? ErrorCode { get; set; }
    
    public ItemDefinition? StructureRef { get; set; }
}