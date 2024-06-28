namespace WebApiEngine.Shared;

public class BpmnMetaDefinitionDto
{
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool Active { get; set; }
}