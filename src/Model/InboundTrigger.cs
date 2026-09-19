namespace Model;

/// <summary>Was ein Aufruf des Auslösers bewirkt.</summary>
public enum InboundTriggerKind
{
    /// <summary>Startet eine neue Instanz des hinterlegten Workflows.</summary>
    Start,

    /// <summary>Stellt einer wartenden Instanz eine BPMN-Nachricht zu.</summary>
    Message
}

/// <summary>
/// Wie aus dem JSON-Körper des Aufrufs Prozessvariablen werden.
/// </summary>
public enum InboundTriggerVariablesMode
{
    /// <summary>
    /// Nur die in <see cref="InboundTrigger.AllowedFields"/> genannten obersten Felder werden
    /// übernommen. Standard, und mit leerer Liste heißt das: keine Variablen. Ein fremdes System
    /// schickt oft seinen ganzen Datensatz; ohne diese Grenze läge er im Prozess.
    /// </summary>
    Fields,

    /// <summary>Der ganze Körper wird als eine Variable <c>payload</c> übernommen.</summary>
    Body
}

/// <summary>
/// Ein von außen aufrufbarer Auslöser. Ein fremdes System (Ticketsystem, Shop, Formulardienst)
/// ruft ihn ohne OIDC-Sitzung auf und weist sich mit einer HMAC-Signatur über Zeitstempel und
/// Körper aus — so, wie Webhook-Anbieter es üblicherweise verlangen.
///
/// Das Geheimnis selbst steht nie hier: <see cref="SecretHash"/> trägt nur seine Ableitung.
/// Gezeigt wird es genau einmal, in der Antwort auf das Anlegen oder das Erneuern.
/// </summary>
public class InboundTrigger
{
    public required Guid Id { get; set; }

    /// <summary>
    /// Der URL-sichere Teil der Adresse <c>POST /trigger/{key}</c>. Zufällig und eindeutig;
    /// er ist kein Geheimnis, verrät aber auch nichts über den Workflow dahinter.
    /// </summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public required InboundTriggerKind Kind { get; set; }

    /// <summary>
    /// Katalogkennung des Workflows bei <see cref="InboundTriggerKind.Start"/>. Gestartet wird
    /// immer die aktuell deployte Version — ein Auslöser bindet sich nicht an eine Fassung.
    /// </summary>
    public string? DefinitionId { get; set; }

    /// <summary>Name der BPMN-Nachricht bei <see cref="InboundTriggerKind.Message"/>.</summary>
    public string? MessageName { get; set; }

    /// <summary>
    /// JSON-Pfad in Punktnotation (<c>order.id</c>), dessen Wert als Korrelationsschlüssel dient.
    /// Nur bei <see cref="InboundTriggerKind.Message"/>.
    /// </summary>
    public string? CorrelationKeyPath { get; set; }

    public InboundTriggerVariablesMode VariablesMode { get; set; } = InboundTriggerVariablesMode.Fields;

    /// <summary>Oberste Felder, die bei <see cref="InboundTriggerVariablesMode.Fields"/> übernommen werden.</summary>
    public string[] AllowedFields { get; set; } = [];

    /// <summary>
    /// Ableitung des Geheimnisses, nie das Geheimnis selbst. Format und Verfahren stehen im
    /// Wert; siehe <c>InboundTriggerSecret</c>.
    /// </summary>
    public required string SecretHash { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedBy { get; set; }

    /// <summary>Wann der Auslöser zuletzt erfolgreich benutzt wurde.</summary>
    public DateTime? LastUsedAt { get; set; }

    public long UseCount { get; set; }

    public DateTime? LastFailureAt { get; set; }

    /// <summary>
    /// Warum der letzte Aufruf abgelehnt wurde — ausschließlich der Grund, nie Daten des
    /// Aufrufers. Wer den Auslöser betreibt, soll sehen, dass jemand mit falscher Signatur
    /// anklopft, ohne dass dessen Nutzdaten dadurch dauerhaft gespeichert werden.
    /// </summary>
    public string? LastFailureReason { get; set; }
}
