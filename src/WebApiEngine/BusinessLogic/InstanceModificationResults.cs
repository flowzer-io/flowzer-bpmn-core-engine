namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Warum eine ganze Eingriffsanfrage abgelehnt wird. Getrennt von den Befunden des Plans:
/// Diese Faelle stehen fest, bevor irgendetwas geprueft oder veraendert wurde, und der
/// Controller bildet sie auf verschiedene HTTP-Statuscodes ab.
/// </summary>
public enum InstanceModificationRequestStatus
{
    /// <summary>Die Anfrage ist zulaessig; das Ergebnis steht in den Befunden.</summary>
    Accepted,

    /// <summary>Die genannte Instanz gibt es nicht.</summary>
    UnknownInstance,

    /// <summary>Die Instanz laeuft nicht mehr; an einem beendeten Vorgang gibt es nichts zu ruecken.</summary>
    InstanceNotRunning,

    /// <summary>Die Anfrage traegt weder eine Verschiebung noch eine Variablenaenderung.</summary>
    NothingToDo,

    /// <summary>Die Anfrage nennt mehr Verschiebungen oder Variablen, als sinnvoll sein kann.</summary>
    RequestTooLarge,

    /// <summary>Der Plan hat Hindernisse; sie stehen als Befunde mit stabilen Codes daneben.</summary>
    NotApplicable,

    /// <summary>Der Eingriff ist unerwartet gescheitert; die Instanz blieb unveraendert.</summary>
    ModificationFailed
}

/// <summary>
/// Ein einzelner Befund. Der Code ist stabil und wird uebersetzt; die englische Meldung ist
/// technische Detailauskunft. Derselbe Typ traegt Hindernisse und Hinweise.
/// </summary>
public sealed record InstanceModificationFinding(string Code, Guid? TokenId, string? FlowNodeId, string Message);

/// <summary>
/// Ein wartender Schritt der Instanz, so wie ihn die Bedienung vor sich hat.
/// </summary>
public sealed record InstanceModificationStep(Guid TokenId, string FlowNodeId, string? Name, string Type);

/// <summary>
/// Ein Knoten des Modells, der als Ziel in Frage kommt. Der Typ ist der Name des
/// BPMN-Elements, damit Konsole und Engine denselben Begriff verwenden.
/// </summary>
public sealed record InstanceModificationFlowNode(string Id, string? Name, string Type);

/// <summary>
/// Der Trockenlauf. Veraendert nichts und beschreibt zugleich, was ueberhaupt moeglich waere:
/// Eine leere Anfrage beantwortet genau die Frage „welche Schritte warten, und wohin duerfen
/// sie?" — die Oberflaeche braucht das, bevor jemand etwas auswaehlt.
/// </summary>
public sealed record InstanceModificationPreview(
    InstanceModificationRequestStatus Status,
    string? Message,
    Guid InstanceId,
    bool Applicable,
    IReadOnlyList<InstanceModificationFinding> Problems,
    IReadOnlyList<InstanceModificationFinding> Notices,
    IReadOnlyList<InstanceModificationStep> Steps,
    IReadOnlyList<InstanceModificationFlowNode> Targets)
{
    public static InstanceModificationPreview Rejected(
        InstanceModificationRequestStatus status, Guid instanceId, string message) =>
        new(status, message, instanceId, false, [], [], [], []);
}

/// <summary>Das Ergebnis des Eingriffs an genau einer Instanz.</summary>
public sealed record InstanceModificationOutcome(
    InstanceModificationRequestStatus Status,
    string? Message,
    Guid InstanceId,
    bool Modified,
    IReadOnlyList<InstanceModificationFinding> Problems,
    IReadOnlyList<InstanceModificationFinding> Notices)
{
    public static InstanceModificationOutcome Rejected(
        InstanceModificationRequestStatus status,
        Guid instanceId,
        string message,
        IReadOnlyList<InstanceModificationFinding>? problems = null) =>
        new(status, message, instanceId, false, problems ?? [], []);
}
