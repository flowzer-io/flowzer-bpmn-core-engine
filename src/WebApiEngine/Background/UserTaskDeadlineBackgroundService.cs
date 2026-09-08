using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

/// <summary>Holt fällige Human-Task-Meilensteine nach Neustarts begrenzt und dedupliziert nach.</summary>
public sealed class UserTaskDeadlineBackgroundService(
    UserTaskDeadlineService deadlines,
    TimeProvider timeProvider,
    UserTaskDeadlinePolicy policy,
    IOptions<UserTaskDeadlineOptions> options,
    ILogger<UserTaskDeadlineBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        if (!configured.Enabled)
        {
            logger.LogInformation("User-task deadline scheduler is disabled.");
            return;
        }

        var backfilled = await deadlines.BackfillMissingAsync(policy, stoppingToken);
        if (backfilled > 0)
            logger.LogInformation("Bound schedules for {Count} existing user task(s).", backfilled);
        await Tick(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configured.PollIntervalSeconds), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await Tick(stoppingToken);
    }

    private async Task Tick(CancellationToken stoppingToken)
    {
        try
        {
            var created = await deadlines.ProcessDueAsync(
                timeProvider.GetUtcNow(), options.Value.BatchSize, stoppingToken);
            if (created > 0) logger.LogInformation("Created {Count} due user-task notification(s).", created);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normales Herunterfahren.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "User-task deadline scheduler tick failed.");
        }
    }
}
