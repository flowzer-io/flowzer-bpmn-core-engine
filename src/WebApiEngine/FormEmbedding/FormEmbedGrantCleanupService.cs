using Microsoft.Extensions.Options;
using StorageSystem;

namespace WebApiEngine.FormEmbedding;

/// <summary>Begrenzte Ablaufbereinigung ohne Engine-, Instanz- oder Aufgabenlock.</summary>
public sealed class FormEmbedGrantCleanupService(
    IStorageSystem storage,
    IOptions<FormEmbeddingOptions> options,
    TimeProvider clock,
    ILogger<FormEmbedGrantCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await storage.FormEmbedGrantStorage.CleanupExpired(clock.GetUtcNow(), stoppingToken);
                await storage.StartFormEmbedGrantStorage.CleanupExpired(clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // Keine Bindungen, SQL-/Dateiinhalte oder Secrets in Betriebslogs.
                logger.LogWarning("Form-link cleanup failed ({FailureType}).", exception.GetType().Name);
            }
        }
    }
}
