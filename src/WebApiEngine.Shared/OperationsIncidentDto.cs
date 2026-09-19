namespace WebApiEngine.Shared;

/// <summary>
/// Eine Störung: etwas, das ohne Eingriff liegen bleibt.
///
/// Störungen werden abgeleitet und nicht gespeichert. Es gibt keine eigene Tabelle, weil es
/// keinen eigenen Zustand gibt: Ein Auftrag ohne verbleibende Versuche und eine gescheiterte
/// Instanz sind bereits vollständig in Ablage und Laufzeit beschrieben. Eine zweite Ablage
/// daneben könnte nur noch veralten.
/// </summary>
public class OperationsIncidentDto
{
    /// <summary>
    /// <see cref="OperationsIncidentKinds.JobExhausted"/> oder
    /// <see cref="OperationsIncidentKinds.InstanceFailed"/>. Bewusst eine Zeichenkette und kein
    /// Enum: Die API serialisiert Enums als Zahlen, und eine Zahl in der Betriebssicht wäre für
    /// jeden Leser eine Nachschlagearbeit.
    /// </summary>
    public required string Kind { get; set; }

    public required Guid InstanceId { get; set; }

    public required string MetaDefinitionId { get; set; }

    public required Guid DefinitionId { get; set; }

    /// <summary>Anzeigename des Workflows; die technische Kennung, wenn es keinen gibt.</summary>
    public required string DefinitionName { get; set; }

    /// <summary>
    /// Der Schritt, an dem es hängt. Bei einer gescheiterten Instanz der Knoten des letzten
    /// Tokens, das nicht mehr weiterkam; <c>null</c>, wenn sich keiner benennen lässt.
    /// </summary>
    public string? FlowNodeId { get; set; }

    public string? FlowNodeName { get; set; }

    /// <summary>Nur bei <c>jobExhausted</c>: der liegen gebliebene Auftrag.</summary>
    public Guid? JobId { get; set; }

    /// <summary>Nur bei <c>jobExhausted</c>: der Typ, nach dem ein Worker fragt.</summary>
    public string? JobType { get; set; }

    /// <summary>
    /// Die letzte Meldung des Workers beziehungsweise die Begründung der gescheiterten Instanz.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Seit wann es hängt (UTC). Bei <c>jobExhausted</c> der Zeitpunkt, ab dem der Auftrag
    /// wieder vergeben worden wäre (<c>RetryAt</c>) — und weil der letzte Fehlschlag keine
    /// Wartezeit mehr setzt, in aller Regel der Anlagezeitpunkt des Auftrags. Bei
    /// <c>instanceFailed</c> der Endezeitpunkt der Instanz, also der letzte Statuswechsel ihrer
    /// Tokens — und für eine Instanz ganz ohne Tokens, die es nicht geben sollte, der
    /// Abrufzeitpunkt, damit die Störung nicht ans Ende der Liste rutscht.
    /// </summary>
    public required DateTime Since { get; set; }

    /// <summary>
    /// Wie oft dieser Auftrag bereits von Hand freigegeben wurde. Nur bei <c>jobExhausted</c>.
    ///
    /// Bewusst nicht „verbrauchte Versuche": Die Engine speichert nur die verbleibenden, und
    /// eine Freigabe setzt sie neu. Wie viele Anläufe es insgesamt gab, ließe sich daraus nur
    /// raten; wie oft jemand eingegriffen hat, steht dagegen in der Auftragshistorie.
    /// </summary>
    public int? ManualRetries { get; set; }

    /// <summary>
    /// Die aktuellen Eingaben des Auftrags, damit die Korrektur sie vorbelegen kann. Nur bei
    /// <c>jobExhausted</c> und nur für die Betriebsrolle, die sie über <c>GET /job</c> ohnehin
    /// sieht.
    /// </summary>
    public Dictionary<string, object?>? Variables { get; set; }
}

/// <summary>Die beiden abgeleiteten Störungsarten.</summary>
public static class OperationsIncidentKinds
{
    /// <summary>Ein Service-Task-Auftrag ohne verbleibende Versuche; er wartet auf einen Eingriff.</summary>
    public const string JobExhausted = "jobExhausted";

    /// <summary>Eine Instanz im Zustand <c>Failed</c>.</summary>
    public const string InstanceFailed = "instanceFailed";
}

/// <summary>
/// Die Störungszähler des Diagnose-Schnappschusses. Die Betriebsseite zeigt damit die Kachel,
/// ohne die ganze Liste zu laden.
/// </summary>
public class OperationsIncidentCountersDto
{
    public required int JobExhausted { get; set; }
    public required int InstanceFailed { get; set; }
}
