using FluentAssertions;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Die reine Rechenkante der Auswertungen: Perzentile und Zeitraum.</summary>
public sealed class WorkflowAnalyticsTest
{
    // Testzweck: Median und p90 werden linear interpoliert (Methode R-7). Bei gerader Anzahl ist
    // der Median deshalb das Mittel der beiden mittleren Werte — die Stichprobe hat bekannte
    // Werte, damit die Kennzahl nicht nur "irgendein" Perzentil ist.
    [Test]
    public void Summarize_ShouldInterpolatePercentilesOfAKnownSample()
    {
        var statistics = DurationStatistics.Summarize([100d, 10d, 30d, 20d]);

        statistics.Should().NotBeNull();
        statistics!.SampleCount.Should().Be(4);
        statistics.MedianSeconds.Should().Be(25d);
        statistics.P90Seconds.Should().BeApproximately(79d, 1e-9);
        statistics.MeanSeconds.Should().Be(40d);
        statistics.MaxSeconds.Should().Be(100d);
    }

    // Testzweck: Bei ungerader Anzahl liegt der Median auf einem echten Messwert und wird nicht
    // durch die Interpolation verschoben.
    [Test]
    public void Summarize_ShouldUseTheMiddleValueOfAnOddSample()
    {
        DurationStatistics.Summarize([5d, 1d, 9d])!.MedianSeconds.Should().Be(5d);
    }

    // Testzweck: Ohne Messwert gibt es keine Dauer. Eine 0 waere an dieser Stelle eine Aussage,
    // die die Daten nicht tragen — die Oberflaeche muss "noch keine" anzeigen koennen.
    [Test]
    public void Summarize_ShouldReportNothingForAnEmptySample()
    {
        DurationStatistics.Summarize([]).Should().BeNull();
    }

    // Testzweck: Eine einzelne Messung ist zugleich Median, p90, Mittel und Maximum.
    [Test]
    public void Summarize_ShouldReportASingleSampleAsEveryStatistic()
    {
        var statistics = DurationStatistics.Summarize([42d])!;

        statistics.MedianSeconds.Should().Be(42d);
        statistics.P90Seconds.Should().Be(42d);
        statistics.MeanSeconds.Should().Be(42d);
        statistics.MaxSeconds.Should().Be(42d);
    }

    // Testzweck: Ohne Angabe wertet die API die letzten 30 Tage bis jetzt aus.
    [Test]
    public void Range_ShouldDefaultToTheLastThirtyDays()
    {
        var now = DateTimeOffset.Parse("2026-09-19T10:00:00Z");

        var range = WorkflowAnalyticsRange.Create(null, null, now);

        range.ToUtc.Should().Be(now);
        range.FromUtc.Should().Be(now.AddDays(-WorkflowAnalyticsRange.DefaultDays));
    }

    // Testzweck: Ein Zeitraum, dessen Ende vor seinem Anfang liegt, ist keine Frage, die sich
    // beantworten laesst; die Meldung sagt das, statt eine leere Auswertung zu liefern.
    [Test]
    public void Range_ShouldRejectAnInvertedPeriod()
    {
        var now = DateTimeOffset.Parse("2026-09-19T10:00:00Z");

        var create = () => WorkflowAnalyticsRange.Create(now, now.AddDays(-1), now);

        create.Should().Throw<WorkflowAnalyticsRequestException>();
    }

    // Testzweck: Die dokumentierte Obergrenze von 366 Tagen gilt, und die Meldung nennt den
    // Ausweg — einen kuerzeren Zeitraum.
    [Test]
    public void Range_ShouldRejectMoreThanTheDocumentedMaximumOfDays()
    {
        var now = DateTimeOffset.Parse("2026-09-19T10:00:00Z");

        var create = () => WorkflowAnalyticsRange.Create(
            now.AddDays(-(WorkflowAnalyticsRange.MaxDays + 1)), now, now);

        create.Should().Throw<WorkflowAnalyticsRequestException>()
            .WithMessage("*shorter period*");
    }

    // Testzweck: Die Tagesliste ist lueckenlos und schliesst den Endzeitpunkt nicht ein —
    // sonst traegt die Zeitreihe einen Tag, von dem keine einzige Sekunde ausgewertet wurde.
    [Test]
    public void Range_ShouldListEveryDayWithoutTheExcludedEnd()
    {
        var range = new WorkflowAnalyticsRange(
            DateTimeOffset.Parse("2026-09-17T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"));

        range.Days().Should().Equal(
            new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 19));
    }
}
