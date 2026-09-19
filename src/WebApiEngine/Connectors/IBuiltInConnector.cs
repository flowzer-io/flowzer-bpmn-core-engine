using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Connectors;

/// <summary>
/// Ein mitgelieferter Worker. Er unterscheidet sich von einem externen Worker nur dadurch, dass
/// er im API-Prozess laeuft: Aufträge holt und meldet er ueber dieselben Mechanismen, und die
/// Engine kennt keinen Sonderpfad fuer ihn.
/// </summary>
public interface IBuiltInConnector
{
    /// <summary>Auftragstyp aus <c>zeebe:taskDefinition/@type</c>, etwa <c>flowzer:http</c>.</summary>
    string JobType { get; }

    /// <summary>Kurzer technischer Name, etwa <c>http</c>; bildet Worker-Kennung und Diagnosezeile.</summary>
    string Name { get; }

    bool Enabled { get; }

    Task<ConnectorOutcome> Execute(ServiceTaskJob job, CancellationToken cancellationToken);
}

/// <summary>
/// Wie ein Auftrag ausgeht. Die drei Faelle bilden genau die drei Rueckmeldungen des
/// Worker-Vertrags ab und halten die Entscheidung im Konnektor, nicht im Host.
/// </summary>
public abstract record ConnectorOutcome
{
    private ConnectorOutcome()
    {
    }

    /// <summary>Der Auftrag ist erledigt; die Werte gehen in den Prozesskontext.</summary>
    public sealed record Completed(Variables? Variables) : ConnectorOutcome;

    /// <summary>
    /// Technisch gescheitert. Bleiben Versuche uebrig, wird der Auftrag nach der Wartezeit
    /// wieder vergeben; sonst bleibt er liegen und wartet auf einen Eingriff.
    /// </summary>
    public sealed record Failed(string Message, TimeSpan Backoff) : ConnectorOutcome;

    /// <summary>
    /// Ein fachliches Ergebnis mit eigenem Weg im Modell. Ein Error-Boundary am Service-Task
    /// faengt es; ein zweiter Versuch aenderte daran nichts.
    /// </summary>
    public sealed record BpmnError(string ErrorCode, string? ErrorMessage, Variables? Variables) : ConnectorOutcome;
}

/// <summary>
/// Technische Identitaet der eingebauten Konnektoren. Sie ist fest und dokumentiert, damit die
/// Sperre eines Auftrags auch im Betriebsbild eindeutig einem Konnektor gehoert und nicht
/// einer beliebigen angemeldeten Person. Die Kennung ist kein Verzeichniskonto: Die
/// Rollenpruefung liegt am Controller, den der eingebaute Host nicht benutzt.
/// </summary>
public static class BuiltInConnectorIdentity
{
    public static readonly Guid UserId = Guid.Parse("f10c2e17-0000-4000-8000-000000000001");

    public static string WorkerId(string connectorName) => $"flowzer-connector-{connectorName}";
}
