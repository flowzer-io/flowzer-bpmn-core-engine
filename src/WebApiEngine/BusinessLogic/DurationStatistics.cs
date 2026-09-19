using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Verteilung einer gemessenen Dauer. Bewusst kein Histogramm und keine Zeitreihendatenbank:
/// Die Kennzahlen entstehen aus der vorhandenen Stichprobe im Speicher.
/// </summary>
public static class DurationStatistics
{
    /// <summary>
    /// Fasst die Messwerte zusammen. Eine leere Stichprobe ergibt <c>null</c> — eine 0 wäre dort
    /// eine Aussage über eine Dauer, die niemand gemessen hat.
    /// </summary>
    public static DurationStatisticsDto? Summarize(IReadOnlyCollection<double> samplesInSeconds)
    {
        ArgumentNullException.ThrowIfNull(samplesInSeconds);
        if (samplesInSeconds.Count == 0) return null;

        var sorted = samplesInSeconds.Order().ToArray();
        return new DurationStatisticsDto
        {
            SampleCount = sorted.Length,
            MedianSeconds = Percentile(sorted, 0.5),
            P90Seconds = Percentile(sorted, 0.9),
            MeanSeconds = sorted.Average(),
            MaxSeconds = sorted[^1]
        };
    }

    /// <summary>
    /// Perzentil mit linearer Interpolation zwischen den benachbarten Rängen (Methode R-7, wie
    /// in Tabellenkalkulationen). Der Median ist damit bei gerader Anzahl das Mittel der beiden
    /// mittleren Werte — genau das, was beim Wort „Median“ erwartet wird. Der Rang-Sprung
    /// („nearest rank“) würde bei kleinen Stichproben stattdessen einen der beiden Werte
    /// bevorzugen und den Median von vier Vorgängen unerklärlich machen.
    /// </summary>
    /// <param name="sorted">Aufsteigend sortierte, nicht leere Stichprobe.</param>
    /// <param name="quantile">Gesuchtes Quantil zwischen 0 und 1.</param>
    public static double Percentile(IReadOnlyList<double> sorted, double quantile)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0) throw new ArgumentOutOfRangeException(nameof(sorted));
        if (quantile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(quantile));
        if (sorted.Count == 1) return sorted[0];

        var position = quantile * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];

        return sorted[lower] + ((position - lower) * (sorted[upper] - sorted[lower]));
    }
}
