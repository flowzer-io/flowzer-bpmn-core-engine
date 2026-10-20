namespace WebApiEngine.BusinessLogic;

/// <summary>Serverseitige, reviewte Versionsbindungen; der leere Standard liefert ausschließlich Unknown.</summary>
public sealed class WorkflowOutcomeOptions
{
    /// <summary>Konfigurationsabschnitt, keine vom Client gelieferte Abbildung.</summary>
    public const string SectionName = "WorkflowOutcomes";

    /// <summary>Explizite Versionsbindungen; ungültige oder doppelte Bindungen schließen die Projektion.</summary>
    public List<WorkflowOutcomeDefinitionOptions> Definitions { get; set; } = [];
}

/// <summary>Eine konkrete Version samt überprüftem XML-Inhalt, nicht latest, Variablen oder Anzeigenamen.</summary>
public sealed class WorkflowOutcomeDefinitionOptions
{
    /// <summary>Exakte Versions-GUID. String, damit fehlerhafte Konfiguration sicher Unknown statt Hostabbruch ergibt.</summary>
    public string DefinitionId { get; set; } = "";
    /// <summary>Exakte Katalogkennung der gespeicherten Definition und Instanz.</summary>
    public string CatalogId { get; set; } = "";
    /// <summary>Exakte ID des einzigen Rootprozesses in der gebundenen BPMN-Datei.</summary>
    public string ProcessId { get; set; } = "";
    /// <summary>Reguläres None-Ende direkt im Rootprozess für die Genehmigung.</summary>
    public string ApprovedRootEndId { get; set; } = "";
    /// <summary>Davon verschiedenes reguläres None-Ende direkt im Rootprozess für die Ablehnung.</summary>
    public string RejectedRootEndId { get; set; } = "";
    /// <summary>64 Hexzeichen: SHA256 exakt über UTF8 der gespeicherten Original-BPMN-XML-Zeichenfolge.</summary>
    public string BpmnSha256 { get; set; } = "";

    internal bool IsValid() => Guid.TryParseExact(DefinitionId, "D", out var id) && id != Guid.Empty
        && ValidId(CatalogId) && ValidId(ProcessId) && ValidId(ApprovedRootEndId) && ValidId(RejectedRootEndId)
        && !string.Equals(ApprovedRootEndId, RejectedRootEndId, StringComparison.Ordinal)
        && BpmnSha256 is { Length: 64 } && BpmnSha256.All(char.IsAsciiHexDigit);

    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && !value.Any(char.IsWhiteSpace) && !value.Contains('*');
}
