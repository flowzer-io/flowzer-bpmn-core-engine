namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Der ausgewertete Zeitraum. <see cref="FromUtc"/> zählt mit, <see cref="ToUtc"/> nicht — so
/// gehört jeder Zeitpunkt zu genau einem Tag und eine Instanz zu genau einem Zeitraum.
/// </summary>
public sealed record WorkflowAnalyticsRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    /// <summary>Ohne Angabe wird der zuletzt vergangene Monat ausgewertet.</summary>
    public const int DefaultDays = 30;

    /// <summary>
    /// Obergrenze des Zeitraums. Die Kennzahlen entstehen im Speicher aus den Ereignissen des
    /// Zeitraums; ohne Deckel bestimmt allein der Aufrufer, wie viel die API dafür lädt.
    /// </summary>
    public const int MaxDays = 366;

    public TimeSpan Duration => ToUtc - FromUtc;

    /// <summary>
    /// Bildet den angefragten Zeitraum. Fehlende Angaben werden ergänzt, unbrauchbare
    /// abgelehnt — die Meldung nennt den Ausweg, nicht nur den Fehler.
    /// </summary>
    public static WorkflowAnalyticsRange Create(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, DateTimeOffset now)
    {
        var to = (toUtc ?? now).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-DefaultDays)).ToUniversalTime();

        if (from >= to)
        {
            throw new WorkflowAnalyticsRequestException(
                "The start of the period must lie before its end.");
        }

        if (to - from > TimeSpan.FromDays(MaxDays))
        {
            throw new WorkflowAnalyticsRequestException(
                $"The period covers more than {MaxDays} days. Please request a shorter period.");
        }

        return new WorkflowAnalyticsRange(from, to);
    }

    /// <summary>Alle Kalendertage des Zeitraums in UTC, lückenlos und aufsteigend.</summary>
    public IEnumerable<DateOnly> Days()
    {
        var day = DateOnly.FromDateTime(FromUtc.UtcDateTime);
        // Ein Zeitraum, der exakt auf Mitternacht endet, schliesst diesen Tag nicht mehr ein:
        // ToUtc zaehlt nicht mit, und ein Tag ohne eine einzige Sekunde waere immer leer.
        var last = DateOnly.FromDateTime(ToUtc.UtcDateTime.AddTicks(-1));
        while (day <= last)
        {
            yield return day;
            day = day.AddDays(1);
        }
    }
}

/// <summary>
/// Die Anfrage selbst ist unbrauchbar — ein zu grosser Zeitraum oder zu viele Ereignisse.
/// Die Controller beantworten das mit 422 und einem Hinweis, was zu ändern ist.
/// </summary>
public sealed class WorkflowAnalyticsRequestException(string message) : Exception(message);
