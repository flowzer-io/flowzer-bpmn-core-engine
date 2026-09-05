using WebApiEngine.Jobs;

namespace WebApiEngine.Background;

/// <summary>
/// Sieht regelmäßig nach freien Aufträgen und benachrichtigt die angemeldeten Worker.
/// Ein Fehler beim Zustellen darf den Dienst nicht beenden; er wird protokolliert und beim
/// nächsten Durchgang erneut versucht.
/// </summary>
public sealed class ServiceTaskWebhookBackgroundService(
    ServiceTaskWebhookNotifier notifier,
    FlowzerWebhookOptions options,
    ILogger<ServiceTaskWebhookBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Benachrichtigung von Service-Task-Workern ist abgeschaltet.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await notifier.NotifyPendingJobs(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durchgang der Worker-Benachrichtigung fehlgeschlagen.");
            }
        }
    }
}
