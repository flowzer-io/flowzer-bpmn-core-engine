namespace WebApiEngine.Shared;

public class BpmnVersionDto
{
    public int Major { get; set; }
    public int Minor { get; set; }
}

public class BpmnMetaDefinitionDto
{
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool Active { get; set; }
}