namespace Model;

public class BpmnMetaDefinition
{
    // references to the DefinitionId of BpmnDefinition
    public required string DefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Ordner, in dem dieser Workflow liegt; <c>null</c> heisst oberste Ebene. Bestehende
    /// Katalogeintraege tragen das Feld nicht und landen deshalb dort — kein Umzug noetig.
    /// </summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// Aufbewahrungsfrist beendeter Instanzen dieses Workflows in Tagen.
    ///
    /// <c>null</c> heisst: Es gilt der installationsweite Wert aus
    /// <c>Retention:FinishedInstances:Days</c>. <c>0</c> heisst: nie loeschen — eine
    /// ausdrueckliche Ausnahme fuer Workflows, deren Vorgaenge aufbewahrungspflichtig sind, und
    /// deshalb bewusst von „kein Wert gesetzt" unterscheidbar.
    ///
    /// Bestehende Katalogeintraege tragen das Feld nicht und laden damit als <c>null</c>, also
    /// mit dem globalen Wert. Bewusst nicht <c>required</c>.
    /// </summary>
    public int? RetentionDays { get; set; }
}

public class ExtendedBpmnMetaDefinition: BpmnMetaDefinition
{
    public Model.Version? LatestVersion { get; set; } = new Model.Version(0,0);
    public DateTime LatestVersionDateTime { get; set; }
    
    public Guid? DeployedId { get; set; }
    public Model.Version? DeployedVersion { get; set; }
    public DateTime? DeployedVersionDateTime { get; set; }
}