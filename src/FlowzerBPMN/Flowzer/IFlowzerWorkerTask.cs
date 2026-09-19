namespace BPMN.Flowzer;

/// <summary>
/// Ein Element, das seine Arbeit an einen externen Worker vergibt: der Service-Task und –
/// seit Fähigkeitsvertrag 6 – die sendenden Nachrichtenelemente mit
/// <c>zeebe:taskDefinition/@type</c>.
///
/// Die Engine führt keines davon selbst aus; sie legt einen Auftrag an und wartet auf
/// Ergebnis oder Fehler. Dieser gemeinsame Vertrag hält die Auftragslogik an einer Stelle,
/// statt sie je Elementart zu verzweigen.
/// </summary>
public interface IFlowzerWorkerTask
{
    /// <summary>Der Auftragstyp. Leer heißt: Dieses Element vergibt keinen Auftrag.</summary>
    string Implementation { get; }

    /// <summary>Wie oft ein fehlgeschlagener Auftrag erneut vergeben wird. 0 heißt: Vorgabe des Hosts.</summary>
    int FlowzerRetries { get; }
}
