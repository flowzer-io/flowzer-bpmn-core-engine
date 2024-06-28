namespace Model;

public class BpmnMetaDefinition
{
    // references to the DefinitionId of BpmnDefinition
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required bool IsActive { get; set; }
}