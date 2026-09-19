using System.Diagnostics;
using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;

namespace WebApiEngine.Background;

/// <summary>
/// Loescht in festem Abstand beendete Instanzen, deren Aufbewahrungsfrist abgelaufen ist.
///
/// Aufgebaut wie der Timer-Scheduler: Zustand in einen Diagnosespeicher melden, ein Lauf sofort
/// beim Start, danach zyklisch. Ein gescheiterter Lauf bricht den Dienst nicht ab — er wird
/// protokolliert, erscheint in der Betriebsdiagnose und der naechste Lauf versucht es erneut.
/// Anders herum stuende nach der ersten Stoerung — etwa einer kurz nicht erreichbaren Datenbank —
/// die Aufbewahrung bis zum naechsten Neustart still, ohne dass es jemandem auffiele.
///
/// Die Uhr kommt aus <see cref="TimeProvider"/>, damit ein Test Fristen von Tagen pruefen kann,
/// ohne zu warten.
/// </summary>
public sealed class InstanceRetentionBackgroundService(
    InstanceRetentionService retention,
    TimeProvider timeProvider,
    IOptions<InstanceRetentionOptions> options,
    InstanceRetentionDiagnosticsState diagnosticsState,
    ILogger<InstanceRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        diagnosticsState.MarkConfigured(
            configured.IsEnabled, configured.Days, configured.PollIntervalMinutes, configured.BatchSize);

        if (!configured.IsEnabled)
        {
            // Ohne Frist gibt es nichts zu tun. Das je Workflow gesetzte `retentionDays` allein
            // aktiviert den Dienst bewusst nicht: Die Aufbewahrung bleibt eine Entscheidung des
            // Betriebs, nicht eine Nebenwirkung eines einzelnen Katalogeintrags.
            logger.LogInformation("Finished-instance retention is disabled.");
            return;
        }

        diagnosticsState.MarkStarted(timeProvider.GetUtcNow().UtcDateTime);
        logger.LogInformation(
            "Finished-instance retention is enabled: {Days} day(s), every {PollIntervalMinutes} minute(s), at most {BatchSize} instance(s) per run.",
            configured.Days, configured.PollIntervalMinutes, configured.BatchSize);

        await RunOnce(stoppingToken);

        using var periodicTimer = new PeriodicTimer(
            TimeSpan.FromMinutes(configured.PollIntervalMinutes), timeProvider);
        while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnce(stoppingToken);
        }
    }

    private async Task RunOnce(CancellationToken stoppingToken)
    {
        using var activity = FlowzerDiagnostics.ActivitySource.StartActivity(
            "instance.retention.run", ActivityKind.Internal);
        var configured = options.Value;
        var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        diagnosticsState.MarkRunStarted(startedAtUtc);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var deleted = await retention.RunAsync(
                configured.Days, startedAtUtc, configured.BatchSize, stoppingToken);
            stopwatch.Stop();
            diagnosticsState.MarkRunSucceeded(
                timeProvider.GetUtcNow().UtcDateTime, stopwatch.Elapsed, deleted);
            activity?.SetTag("flowzer.retention.deleted_instances", deleted);
            activity?.SetTag("flowzer.retention.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

            // Eine Zeile je Lauf mit etwas zu melden, ohne Kennungen, Namen oder Personenbezug:
            // Der Betrieb soll sehen, dass und wie viel geloescht wurde — nicht wessen Vorgang.
            if (deleted > 0)
                logger.LogInformation("Deleted {DeletedInstances} finished process instance(s) past their retention period.", deleted);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normales Herunterfahren des Host-Prozesses.
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            // Wie viele vor dem Fehler geloescht wurden, weiss nur der Dienst selbst nicht mehr;
            // die bereits committeten Loeschungen bleiben bestehen und werden im naechsten
            // erfolgreichen Lauf nicht erneut gezaehlt.
            diagnosticsState.MarkRunFailed(
                timeProvider.GetUtcNow().UtcDateTime, stopwatch.Elapsed, exception, deletedInstances: 0);
            activity?.SetTag("flowzer.retention.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            logger.LogError(exception, "Finished-instance retention run failed.");
        }
    }
}
