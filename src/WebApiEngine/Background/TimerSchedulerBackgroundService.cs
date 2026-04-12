using System.Diagnostics;
using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;

namespace WebApiEngine.Background;

public class TimerSchedulerBackgroundService(
    BpmnBusinessLogic bpmnBusinessLogic,
    IOptions<TimerSchedulerOptions> options,
    TimerSchedulerDiagnosticsState diagnosticsState,
    ILogger<TimerSchedulerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedulerOptions = options.Value;
        var pollIntervalSeconds = Math.Max(1, schedulerOptions.PollIntervalSeconds);
        diagnosticsState.MarkConfigured(schedulerOptions.Enabled, pollIntervalSeconds);
        if (!schedulerOptions.Enabled)
        {
            logger.LogInformation("Timer scheduler is disabled.");
            return;
        }

        diagnosticsState.MarkStarted(DateTime.UtcNow);
        logger.LogInformation(
            "Timer scheduler is enabled with a poll interval of {PollIntervalSeconds} second(s).",
            pollIntervalSeconds);

        var pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);

        await RunTimerTick(stoppingToken);

        using var periodicTimer = new PeriodicTimer(pollInterval);
        while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            await RunTimerTick(stoppingToken);
        }
    }

    private async Task RunTimerTick(CancellationToken stoppingToken)
    {
        using var activity = FlowzerDiagnostics.ActivitySource.StartActivity("timer.scheduler.tick", ActivityKind.Internal);
        var startedAtUtc = DateTime.UtcNow;
        diagnosticsState.MarkTickStarted(startedAtUtc);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var processedTimers = await bpmnBusinessLogic.HandleTime(DateTime.UtcNow);
            stopwatch.Stop();
            diagnosticsState.MarkTickSucceeded(DateTime.UtcNow, stopwatch.Elapsed, processedTimers);
            FlowzerDiagnostics.RecordTimerSchedulerSuccess(processedTimers, stopwatch.Elapsed);
            activity?.SetTag("flowzer.processed_timers", processedTimers);
            activity?.SetTag("flowzer.tick.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            if (processedTimers > 0)
            {
                logger.LogInformation("Processed {ProcessedTimers} due timer subscriptions.", processedTimers);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normales Herunterfahren des Host-Prozesses.
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            diagnosticsState.MarkTickFailed(DateTime.UtcNow, stopwatch.Elapsed, exception);
            FlowzerDiagnostics.RecordTimerSchedulerFailure(stopwatch.Elapsed);
            activity?.SetTag("flowzer.tick.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            logger.LogError(exception, "Timer scheduler tick failed.");
        }
    }
}
