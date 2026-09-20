namespace WebApiEngine.Shared;

public class HealthStatusDto
{
    public required string Status { get; set; }
    public required DateTime CheckedAtUtc { get; set; }
    public required string Environment { get; set; }
    public required string Storage { get; set; }

    /// <summary>
    /// Nur bei der Bereitschaftsprobe gefuellt und bewusst additiv: ohne diese Angabe meldet ein
    /// Knoten "Healthy", auch wenn er noch auf einem aelteren Schema laeuft. Enthaelt ausschliesslich
    /// nicht geheime Betriebsangaben - keine Verbindungszeichenfolgen und keine Anmeldedaten.
    /// </summary>
    public HealthReadinessDetailsDto? Details { get; set; }
}

/// <summary>Betriebsangaben der Bereitschaftsprobe: welche Ablage laeuft und auf welchem Stand.</summary>
public class HealthReadinessDetailsDto
{
    /// <summary>Beschreibung der konfigurierten Ablage, z. B. <c>Filesystem</c>.</summary>
    public required string StorageProvider { get; set; }

    /// <summary>
    /// <c>UpToDate</c>, <c>Pending</c>, <c>NotApplicable</c> (Dateiablage) oder <c>Unknown</c>,
    /// wenn die Historie gerade nicht lesbar ist. <c>Unknown</c> macht den Knoten nicht unbereit.
    /// </summary>
    public required string MigrationState { get; set; }

    /// <summary>Anzahl noch nicht angewendeter Migrationen; null, wenn der Stand unbekannt ist.</summary>
    public int? PendingMigrationCount { get; set; }

    /// <summary>Hoechste eingebettete Migrationsversion dieses Stands; null bei der Dateiablage.</summary>
    public int? ExpectedMigrationVersion { get; set; }

    /// <summary>
    /// <c>Feel</c>, wenn Ausdruecke ueber libfeelin in V8 ausgewertet werden, sonst
    /// <c>Simple</c>. Der einfache Handler ist kein FEEL: Fehlt die native V8-Bibliothek der
    /// Plattform, laeuft die Installation weiter, rechnet Bedingungen und Zuweisungen aber
    /// nach einer stark vereinfachten Regel. Das sieht man einem laufenden Prozess nicht an -
    /// deshalb steht es hier.
    /// </summary>
    public required string ExpressionEngine { get; set; }
}
