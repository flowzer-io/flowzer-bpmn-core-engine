namespace WebApiEngine.Shared;

public class BpmnMetaDefinitionDto
{
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Ordner, in dem der Workflow liegt; <c>null</c> heisst oberste Ebene. Beim Anlegen
    /// (<c>POST /definition/meta</c>) waehlt dieser Wert den Ordner. Beim Aendern
    /// (<c>PUT /definition/meta</c>) wird er nicht ausgewertet — verschoben wird ueber
    /// <c>PUT /definition/meta/{id}/folder</c>.
    /// </summary>
    public Guid? FolderId { get; set; }
}
public class ExtendedBpmnMetaDefinitionDto: BpmnMetaDefinitionDto
{
    public VersionDto? LatestVersion { get; set; } 
    public DateTime LatestVersionDateTime { get; set; }
    
    public Guid? DeployedId { get; set; }
    public VersionDto? DeployedVersion { get; set; }
    
    public DateTime DeployedVersionDateTime { get; set; }
    
    
}