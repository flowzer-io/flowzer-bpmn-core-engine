namespace WebApiEngine.Shared;

public class ProcessInfoDto
{
    public required string Id { get; set; }
    public string? Name { get; set; }
    public required string DefinitionId { get; set; }
}