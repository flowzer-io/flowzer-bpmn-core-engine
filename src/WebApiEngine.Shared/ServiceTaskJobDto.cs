using System.Dynamic;
using System.Text.Json.Serialization;
namespace WebApiEngine.Shared;

/// <summary>Ein Auftrag, wie ihn ein externer Worker sieht.</summary>
public class ServiceTaskJobDto
{
    public required Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Name { get; set; }
    public required Guid ProcessInstanceId { get; set; }
    public required string ProcessId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string MetaDefinitionId { get; set; }
    public Guid TokenId { get; set; }
    public string? FlowNodeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public int Retries { get; set; }
    public DateTime? RetryAt { get; set; }
    public string? LastErrorMessage { get; set; }

    /// <summary>Eingabewerte des Tokens; der Worker arbeitet damit.</summary>
    public Dictionary<string, object?> Variables { get; set; } = [];
}

/// <summary>Anforderung eines Workers, Aufträge eines Typs zu übernehmen.</summary>
public class FetchJobsRequestDto
{
    public required string Type { get; set; }

    /// <summary>Kennung des Workers; sie muss beim Zurückmelden wieder passen.</summary>
    public required string WorkerId { get; set; }

    public int MaxJobs { get; set; } = 10;

    /// <summary>
    /// Wie lange der Auftrag dem Worker gehört. Läuft die Frist ab, wird er wieder vergeben.
    /// </summary>
    public int LockSeconds { get; set; } = 300;
}

public class CompleteJobRequestDto
{
    public required string WorkerId { get; set; }

    /// <summary>
    /// Das Ergebnis des Workers. Bewusst mit demselben Konverter wie beim User-Task und bei
    /// der Nachricht: Ohne ihn stehen in den Werten <c>JsonElement</c>-Huellen statt Zeichen-
    /// ketten und Zahlen. Die ueberleben die Ablage nicht — gespeichert wird nur noch
    /// <c>{"ValueKind": 3}</c>, und jede spaetere Bedingung auf so einen Wert ist falsch.
    /// </summary>
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Variables { get; set; }
}

public class FailJobRequestDto
{
    public required string WorkerId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Verbleibende Versuche; ohne Angabe wird um eins verringert.</summary>
    public int? Retries { get; set; }

    /// <summary>Wartezeit vor der nächsten Vergabe.</summary>
    public int RetryBackoffSeconds { get; set; } = 30;
}

public class ServiceTaskWebhookDto
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Url { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Wird nur bei der Anmeldung übergeben und nie zurückgeliefert. Die Benachrichtigung
    /// trägt eine damit gebildete Signatur.
    /// </summary>
    public string? Secret { get; set; }
}
