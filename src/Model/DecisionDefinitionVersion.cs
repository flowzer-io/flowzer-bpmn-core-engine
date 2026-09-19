namespace Model;

/// <summary>
/// Ein unveraenderlicher XML-Stand einer Entscheidungsdatei.
/// </summary>
/// <remarks>
/// Ein gespeicherter Stand wird nie wieder geschrieben: Eine Aenderung legt die naechste
/// Version an. Entwuerfe gibt es in dieser Stufe nicht — deployt ist immer die juengste
/// Version, und genau die rechnet ein Business-Rule-Task.
/// </remarks>
/// <param name="DecisionDefinitionId">Die Katalogkennung der Datei.</param>
/// <param name="Version">Die fortlaufende Versionsnummer, beginnend bei 1.</param>
/// <param name="Xml">Das vollstaendige DMN-Dokument dieses Standes.</param>
/// <param name="DeployedAt">Der Zeitpunkt, zu dem dieser Stand gespeichert wurde.</param>
/// <param name="DeployedBy">
/// Die Person, die den Stand gespeichert hat. <c>null</c> bei einem Stand ohne aufgeloesten
/// Benutzerkontext — etwa in einer Installation ohne Anmeldung.
/// </param>
/// <param name="Decisions">Die in diesem Stand enthaltenen Entscheidungen in Dokumentreihenfolge.</param>
public sealed record DecisionDefinitionVersion(
    string DecisionDefinitionId,
    int Version,
    string Xml,
    DateTimeOffset DeployedAt,
    Guid? DeployedBy,
    IReadOnlyList<DecisionSummary> Decisions);
