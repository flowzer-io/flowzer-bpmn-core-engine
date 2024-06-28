namespace WebApiEngine.Shared;

public class BpmnDefinitionDto
{
    public required Guid Id { get; set; }
    public required string DefinitionId { get; set; }
    public Guid? PreviousGuid { get; set; }
    public required string Hash { get; set; }
    public required Guid SavedByUser { get; set; }
    public DateTime SavedOn { get; set; }
    public Guid? DeployedByUser { get; set; }
    public DateTime? DeployedOn { get; set; }
    public required BpmnVersionDto Version { get; set; }
}