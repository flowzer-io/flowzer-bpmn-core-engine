using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

public class TimerSchedulerBackgroundService(
    BpmnBusinessLogic bpmnBusinessLogic,
    IOptions<TimerSchedulerOptions> options,
    ILogger<TimerSchedulerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedulerOptions = options.Value;
        if (!schedulerOptions.Enabled)
        {
            logger.LogInformation("Timer scheduler is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, schedulerOptions.PollIntervalSeconds));

        await RunTimerTick(stoppingToken);

        using var periodicTimer = new PeriodicTimer(pollInterval);
        while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            await RunTimerTick(stoppingToken);
        }
    }

    private async Task RunTimerTick(CancellationToken stoppingToken)
    {
        try
        {
            var processedTimers = await bpmnBusinessLogic.HandleTime(DateTime.UtcNow);
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
            logger.LogError(exception, "Timer scheduler tick failed.");
        }
    }
}
