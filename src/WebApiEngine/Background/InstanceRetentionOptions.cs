namespace WebApiEngine.Background;

/// <summary>
/// Aufbewahrung beendeter Instanzen. Konfigurationsabschnitt
/// <c>Retention:FinishedInstances</c>, als Umgebungsvariablen in der ueblichen Doppelunterstrich-
/// Form: <c>Retention__FinishedInstances__Days</c> und so fort.
///
/// Der Default ist <b>aus</b>. Eine Aufbewahrungsfrist ist eine Entscheidung des Betreibers ueber
/// fremde Vorgangsdaten; sie darf nicht durch ein Update in eine Installation kommen, die nie
/// darum gebeten hat.
/// </summary>
public sealed class InstanceRetentionOptions
{
    public const string SectionName = "Retention:FinishedInstances";

    /// <summary>
    /// Frist in Tagen, nach der eine beendete Instanz geloescht wird. <c>null</c> oder <c>0</c>
    /// schalten die Aufbewahrung ab — dann laeuft der Dienst gar nicht erst an. Je Workflow
    /// ueberschreibbar ueber das Definitionsmetadatum <c>retentionDays</c>.
    /// </summary>
    public int? Days { get; set; }

    /// <summary>Abstand zweier Laeufe in Minuten. Eine Frist in Tagen braucht keine Sekundenlage.</summary>
    public int PollIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Hoechstzahl der je Lauf geloeschten Instanzen. Begrenzt, damit ein erstmalig aktivierter
    /// Aufbewahrungslauf einen grossen Altbestand nicht in einem Zug durch die Datenbank treibt.
    /// Der naechste Lauf macht weiter.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Ist ueberhaupt eine Frist gesetzt? <c>0</c> heisst hier wie sonst „nie loeschen".</summary>
    public bool IsEnabled => Days is > 0;

    public bool IsValid() =>
        Days is null or >= 0
        && PollIntervalMinutes is >= 1 and <= 10_080
        && BatchSize is >= 1 and <= 10_000;
}
