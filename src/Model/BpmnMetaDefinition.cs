namespace Model;

public class BpmnMetaDefinition
{
    // references to the DefinitionId of BpmnDefinition
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    
}

public class ExtendedBpmnMetaDefinition: BpmnMetaDefinition
{
    public Model.Version? LatestVersion { get; set; } = new Model.Version(0,0);
    public DateTime LatestVersionDateTime { get; set; }
    
    public Guid? DeployedId { get; set; }
    public Model.Version? DeployedVersion { get; set; }
    public DateTime? DeployedVersionDateTime { get; set; }
}