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
    public required VersionDto Version { get; set; }

    /// <summary>
    /// Hinweise der Modellprüfung zu genau dieser Fassung — Befunde, die das Speichern oder
    /// Veröffentlichen ausdrücklich nicht verhindert haben. Leer, wo nichts zu melden ist;
    /// ältere Clients ignorieren das Feld.
    /// </summary>
    public IReadOnlyList<BpmnCapabilityIssueDto> Warnings { get; set; } = [];
}