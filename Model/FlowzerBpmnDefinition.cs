namespace Model;

public class BpmnDefinition
{
    public required Guid Id { get; set; }
    
    // Is the same on all "same definitions" but the version and then the id may differ
    public required string DefinitionId { get; set; }
    
    // the previous guid of the definition, if it was based on another definition
    public Guid? PreviousGuid { get; set; }

    public required string Hash { get; set; }
    
    public required Guid SavedByUser { get; set; }
    public DateTime SavedOn { get; set; }
    
    public Guid? DeployedByUser { get; set; }
    public DateTime? DeployedOn { get; set; }
    public required BpmnVersion Version { get; set; }
}