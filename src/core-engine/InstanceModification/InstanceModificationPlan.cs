using BPMN.Common;

namespace core_engine;

/// <summary>
/// Das Pruefergebnis eines geplanten Eingriffs. Der Plan ist rein beschreibend: Er haelt die
/// Instanz, die aufgeloesten Verschiebungen und die Variablenaenderungen fest, damit die
/// aufrufende Schicht daran ablesen kann, was verschwindet (eine Aufgabe, ein Auftrag) — ohne
/// die Aufloesung erneut zu erraten.
/// </summary>
public sealed record InstanceModificationPlan
{
    internal InstanceModificationPlan(
        InstanceEngine instance,
        InstanceModificationRequest request,
        IReadOnlyList<PlannedMove> moves,
        IReadOnlyList<InstanceModificationProblem> problems,
        IReadOnlyList<InstanceModificationNotice> notices)
    {
        Instance = instance;
        Request = request;
        Moves = moves;
        Problems = problems;
        Notices = notices;
    }

    /// <summary>Die Instanz, an der der Eingriff stattfinden wuerde.</summary>
    public InstanceEngine Instance { get; }

    /// <summary>Die geprüfte Anfrage, unveraendert.</summary>
    public InstanceModificationRequest Request { get; }

    /// <summary>Die aufgeloesten Verschiebungen in der Reihenfolge der Anfrage.</summary>
    public IReadOnlyList<PlannedMove> Moves { get; }

    /// <summary>Alle Hindernisse; die Oberflaeche listet sie vollstaendig auf.</summary>
    public IReadOnlyList<InstanceModificationProblem> Problems { get; }

    /// <summary>Was der Eingriff mitnimmt, ohne ihn zu verhindern.</summary>
    public IReadOnlyList<InstanceModificationNotice> Notices { get; }

    public bool IsApplicable => Problems.Count == 0;

    /// <summary>
    /// Ein aufgeloester Eingriff: der wartende Quell-Token und der Knoten desselben Modells, an
    /// dem der neue Token beginnt. Quelle und Ziel duerfen derselbe Knoten sein — dann startet
    /// der Schritt neu.
    /// </summary>
    public sealed record PlannedMove(Token SourceToken, FlowNode TargetFlowNode);
}

/// <summary>
/// Ein einzelnes Hindernis. Die Meldung ist fuer Protokoll und API-Detail gedacht; die
/// Oberflaeche uebersetzt den Code.
/// </summary>
public sealed record InstanceModificationProblem(
    InstanceModificationProblemCode Code,
    Guid? TokenId,
    string? FlowNodeId,
    string Message);

/// <summary>
/// Ein Hinweis: Er verhindert den Eingriff nicht, aber niemand soll ihn erst hinterher merken.
/// </summary>
public sealed record InstanceModificationNotice(
    InstanceModificationNoticeCode Code,
    Guid? TokenId,
    string? FlowNodeId,
    string Message);

public enum InstanceModificationProblemCode
{
    /// <summary>Kein eindeutiger, aktiver Master-Token mit einem Prozessmodell.</summary>
    InstanceNotRunning,

    /// <summary>Die genannte Token-Kennung gibt es in dieser Instanz nicht.</summary>
    TokenMissing,

    /// <summary>
    /// Der Token wartet nicht oder steht nicht auf der obersten Prozessebene. Teilprozesse und
    /// Multi-Instance bleiben dieser Stufe verschlossen.
    /// </summary>
    TokenNotMovable,

    /// <summary>Derselbe Quell-Token wurde mehr als einmal genannt.</summary>
    DuplicateMove,

    /// <summary>Den Zielknoten gibt es auf der obersten Ebene dieses Modells nicht.</summary>
    TargetMissing,

    /// <summary>
    /// Der Zielknoten ist kein erlaubtes Ziel: Start-Events und Boundary-Events beginnen nicht
    /// auf einem Sequenzfluss und liessen sich nicht sinnvoll betreten.
    /// </summary>
    TargetNotAllowed,

    /// <summary>
    /// Der Variablenname ist leer oder benennt einen Pfad. Diese Stufe schreibt und entfernt
    /// ausschliesslich ganze Variablen der obersten Ebene.
    /// </summary>
    VariableNameInvalid
}

public enum InstanceModificationNoticeCode
{
    /// <summary>
    /// Am Quellknoten wartet eine Benutzeraufgabe. Sie verschwindet samt Kennung, Uebernahme
    /// und Fristen; am Ziel entsteht gegebenenfalls eine neue mit neuer Kennung.
    /// </summary>
    UserTaskCancelled,

    /// <summary>Am Quellknoten wartet ein Service-Task; sein Auftrag verfaellt.</summary>
    ServiceTaskJobCancelled,

    /// <summary>Ein privater Entwurf der verschwindenden Aufgabe geht verloren.</summary>
    UserTaskDraftDiscarded,

    /// <summary>
    /// Am Zielknoten haengt ein Timer. Er rechnet ab dem Eingriff neu, nicht ab dem
    /// urspruenglichen Beginn des Wartens.
    /// </summary>
    TimerRecalculated,

    /// <summary>Eine zu entfernende Variable gibt es nicht; das ist kein Fehler.</summary>
    VariableNotFound
}
