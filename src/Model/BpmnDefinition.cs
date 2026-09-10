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
    public required Version Version { get; set; }

    public required bool IsActive { get; set; }

    /// <summary>
    /// Deployment-Snapshots nach ursprünglichem Form-Key. Null kennzeichnet einen
    /// historischen Stand ohne belegte Bindung; leer bedeutet bewusst keine Formulare.
    /// Nicht aus einem Client-DTO übernehmen oder beim Wiederaktivieren neu auflösen.
    /// </summary>
    public Dictionary<string, BoundForm>? FormBindings { get; set; }

    /// <summary>
    /// Deployment-Snapshots der KI-Verbindungsrevision und des effektiven Modells nach
    /// Flow-Node-ID. Null kennzeichnet einen historischen Stand ohne KI-Runtime-Bindung;
    /// die Werte werden weder aus einem Client-DTO uebernommen noch beim Start neu aufgeloest.
    /// </summary>
    public Dictionary<string, BoundAiTask>? AiTaskBindings { get; set; }
}
