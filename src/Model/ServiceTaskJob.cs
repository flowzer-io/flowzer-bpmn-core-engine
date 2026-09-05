namespace Model;

/// <summary>
/// Ein wartender Service-Task als Arbeitsauftrag für einen externen Worker.
///
/// Die Engine führt Service-Tasks nicht selbst aus: Sie hätte dafür Netzwerkzugriff,
/// Zugangsdaten und eine eigene Wiederholungslogik nötig. Stattdessen wird jeder wartende
/// Service-Task als Auftrag sichtbar, den ein Worker holt, bearbeitet und zurückmeldet.
/// </summary>
public class ServiceTaskJob
{
    public required Guid Id { get; set; }

    /// <summary>Typ aus <c>zeebe:taskDefinition/@type</c>; danach fragt ein Worker.</summary>
    public required string Type { get; set; }

    public required string Name { get; set; }

    /// <summary>Der wartende Token; er verbindet den Auftrag mit der Instanz.</summary>
    public required Token Token { get; set; }

    public required Guid ProcessInstanceId { get; set; }
    public required string MetaDefinitionId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string ProcessId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bis wann dieser Auftrag einem Worker gehört. Läuft die Frist ab, ohne dass er
    /// zurückgemeldet hat, wird der Auftrag wieder vergeben; ein abgestürzter Worker blockiert
    /// die Instanz damit nicht dauerhaft.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    public string? LockedBy { get; set; }

    /// <summary>Verbleibende Versuche. Bei 0 wartet der Auftrag auf einen Eingriff.</summary>
    public int Retries { get; set; }

    /// <summary>Frühestens ab diesem Zeitpunkt wieder vergeben (Wartezeit nach einem Fehler).</summary>
    public DateTime? RetryAt { get; set; }

    public string? LastErrorMessage { get; set; }

    /// <summary>Eingabewerte des Tokens zum Zeitpunkt der Anlage.</summary>
    public Variables? Variables { get; set; }

    public bool IsAvailableAt(DateTime moment) =>
        (LockedUntil is null || LockedUntil <= moment)
        && (RetryAt is null || RetryAt <= moment)
        && Retries > 0;
}

/// <summary>
/// Ein Worker, der über einen Webhook benachrichtigt werden möchte, statt zu fragen.
/// Gemeldet wird nur, dass ein Auftrag vorliegt; geholt und zurückgemeldet wird über
/// dieselben Endpunkte wie beim Abholen.
/// </summary>
public class ServiceTaskWebhook
{
    public required Guid Id { get; set; }
    public required string Type { get; set; }
    public required Uri Url { get; set; }

    /// <summary>
    /// Gemeinsames Geheimnis. Die Benachrichtigung trägt eine damit gebildete Signatur,
    /// damit der Worker erkennt, dass sie von dieser Flowzer-Installation stammt.
    /// </summary>
    public string? Secret { get; set; }

    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }

    /// <summary>Aufeinanderfolgende Fehlversuche; steuert die Wartezeit vor dem nächsten.</summary>
    public int ConsecutiveFailures { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
