using WebApiEngine.Ai;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

/// <summary>Begrenzter Taktgeber fuer die standardmaessig deaktivierte KI-Ausfuehrung.</summary>
internal sealed class AiRunBackgroundService(
    AiRunExecutor executor,
    BpmnBusinessLogic businessLogic,
    AiRunExecutionPolicy policy,
    TimeProvider timeProvider,
    ILogger<AiRunBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!policy.Enabled)
        {
            logger.LogInformation("AI run execution is disabled.");
            return;
        }

        await Tick(stoppingToken);
        using var timer = new PeriodicTimer(policy.PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await Tick(stoppingToken);
    }

    private async Task Tick(CancellationToken stoppingToken)
    {
        try
        {
            var result = await executor.RunProviderBatchAsync(stoppingToken);
            var engine = await businessLogic.CompleteAiRunBatchAsync(
                timeProvider,
                policy,
                stoppingToken);
            if (result.Claimed > 0 || result.Recovered > 0)
            {
                logger.LogInformation(
                    "AI run tick recovered {Recovered}, claimed {Claimed}, prepared {Ready}, "
                    + "scheduled {Retries}, incidented {Incidents}, and lost {Lost} lease(s).",
                    result.Recovered,
                    result.Claimed,
                    result.ResultsReady,
                    result.RetriesScheduled,
                    result.Incidents,
                    result.LeasesLost);
            }
            if (engine.Claimed > 0)
            {
                logger.LogInformation(
                    "AI engine result tick claimed {Claimed}, completed {Completed}, and incidented {Incidents} run(s).",
                    engine.Claimed,
                    engine.Completed,
                    engine.Incidents);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normales Herunterfahren des Hosts.
        }
        catch (Exception)
        {
            // Infrastrukturfehler koennen Ziel- oder Verbindungsdetails enthalten. Der
            // Hintergrunddienst protokolliert deshalb nur die stabile Durchgangsmeldung.
            logger.LogError("AI run execution tick failed.");
        }
    }
}
