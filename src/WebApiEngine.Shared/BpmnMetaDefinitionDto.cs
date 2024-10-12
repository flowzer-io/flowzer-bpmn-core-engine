namespace WebApiEngine.Shared;

public class BpmnMetaDefinitionDto
{
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}
public class ExtendedBpmnMetaDefinitionDto: BpmnMetaDefinitionDto
{
    public VersionDto? LatestVersion { get; set; } 
    public DateTime LatestVersionDateTime { get; set; }
    
    public Guid? DeployedId { get; set; }
    public VersionDto? DeployedVersion { get; set; }
    
    public DateTime DeployedVersionDateTime { get; set; }
    
    
}