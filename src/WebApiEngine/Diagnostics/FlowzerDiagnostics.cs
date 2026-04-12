using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WebApiEngine.Diagnostics;

/// <summary>
/// Kapselt die kleinen, lokal nutzbaren Metrics-/Tracing-Namen für die Web-API.
/// Die eigentliche Export-Story bleibt bewusst ein Folgepaket; dieses Paket liefert
/// zuerst stabile Signalnamen und einfache Messpunkte.
/// </summary>
public static class FlowzerDiagnostics
{
    public const string MeterName = "Flowzer.WebApi";
    public const string ActivitySourceName = "Flowzer.WebApi";

    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Counter<long> HttpRequestCounter =
        Meter.CreateCounter<long>("flowzer.http.requests");

    private static readonly Histogram<double> HttpRequestDurationMilliseconds =
        Meter.CreateHistogram<double>("flowzer.http.request.duration", "ms");

    private static readonly Counter<long> TimerSchedulerTickCounter =
        Meter.CreateCounter<long>("flowzer.timer.scheduler.ticks");

    private static readonly Counter<long> TimerSchedulerFailureCounter =
        Meter.CreateCounter<long>("flowzer.timer.scheduler.failures");

    private static readonly Histogram<double> TimerSchedulerTickDurationMilliseconds =
        Meter.CreateHistogram<double>("flowzer.timer.scheduler.tick.duration", "ms");

    private static readonly Histogram<int> TimerSchedulerProcessedTimers =
        Meter.CreateHistogram<int>("flowzer.timer.scheduler.processed_timers");

    public static void RecordHttpRequest(string method, string path, int statusCode, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "http.method", method },
            { "url.path", path },
            { "http.status_code", statusCode }
        };

        HttpRequestCounter.Add(1, tags);
        HttpRequestDurationMilliseconds.Record(duration.TotalMilliseconds, tags);
    }

    public static void RecordTimerSchedulerSuccess(int processedTimers, TimeSpan duration)
    {
        TimerSchedulerTickCounter.Add(1);
        TimerSchedulerTickDurationMilliseconds.Record(duration.TotalMilliseconds);
        TimerSchedulerProcessedTimers.Record(processedTimers);
    }

    public static void RecordTimerSchedulerFailure(TimeSpan duration)
    {
        TimerSchedulerTickCounter.Add(1);
        TimerSchedulerFailureCounter.Add(1);
        TimerSchedulerTickDurationMilliseconds.Record(duration.TotalMilliseconds);
    }
}
