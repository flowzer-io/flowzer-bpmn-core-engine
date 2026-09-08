using Microsoft.Extensions.Options;

namespace WebApiEngine.IdentityDirectory;

/// <summary>Startet den opt-in Verzeichnisabgleich sofort und danach in einem begrenzten Intervall.</summary>
public sealed class IdentityDirectoryBackgroundService(
    IdentityDirectorySynchronizer synchronizer,
    IOptions<KeycloakDirectoryOptions> options,
    ILogger<IdentityDirectoryBackgroundService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _manualSignal = new(0, 1);
    // 0 = frei, 1 = manueller/periodischer Lauf vorgemerkt oder aktiv. Ein gemeinsamer Zustand
    // schliesst auch die kleine Luecke zwischen dem Leeren des Signals und dem Laufstart.
    private int _synchronizationReserved;

    public enum ManualTriggerOutcome
    {
        Disabled,
        Accepted,
        Busy
    }

    /// <summary>
    /// Stellt hoechstens einen manuellen Lauf in die lokale Warteschlange. Der HTTP-Aufruf
    /// bleibt dadurch kurz; Ergebnis und Fehler werden anschließend ueber den Status beobachtet.
    /// </summary>
    public ManualTriggerOutcome RequestSynchronization()
    {
        if (!options.Value.Enabled) return ManualTriggerOutcome.Disabled;
        if (Interlocked.CompareExchange(ref _synchronizationReserved, 1, 0) != 0)
        {
            return ManualTriggerOutcome.Busy;
        }
        try
        {
            _manualSignal.Release();
            return ManualTriggerOutcome.Accepted;
        }
        catch (SemaphoreFullException)
        {
            Volatile.Write(ref _synchronizationReserved, 0);
            return ManualTriggerOutcome.Busy;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directoryOptions = options.Value;
        if (!directoryOptions.Enabled)
        {
            logger.LogInformation("Keycloak directory synchronization is disabled.");
            return;
        }

        if (Interlocked.CompareExchange(ref _synchronizationReserved, 1, 0) == 0)
        {
            await RunSynchronizationAsync(stoppingToken);
        }
        var interval = TimeSpan.FromSeconds(Math.Clamp(directoryOptions.SyncIntervalSeconds, 10, 86_400));
        while (!stoppingToken.IsCancellationRequested)
        {
            var manuallyRequested = false;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(interval);
            try
            {
                await _manualSignal.WaitAsync(wait.Token);
                manuallyRequested = true;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Das Intervall ist abgelaufen; derselbe Pfad startet den periodischen Lauf.
            }

            if (!manuallyRequested && Interlocked.CompareExchange(ref _synchronizationReserved, 1, 0) != 0)
            {
                continue;
            }
            await RunSynchronizationAsync(stoppingToken);
        }
    }

    private async Task RunSynchronizationAsync(CancellationToken stoppingToken)
    {
        try
        {
            await synchronizer.TrySynchronizeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Geregeltes Beenden des Hosts.
        }
        finally
        {
            Volatile.Write(ref _synchronizationReserved, 0);
        }
    }
}
