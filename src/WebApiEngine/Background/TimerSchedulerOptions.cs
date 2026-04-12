namespace WebApiEngine.Background;

public class TimerSchedulerOptions
{
    public const string SectionName = "TimerScheduler";

    /// <summary>
    /// Aktiviert oder deaktiviert den Hintergrund-Poller für fällige Timer.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Poll-Intervall in Sekunden. Kleine Default-Werte erleichtern lokale Tests,
    /// ohne in CI künstlich aggressive Loops zu erzeugen.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 5;
}
