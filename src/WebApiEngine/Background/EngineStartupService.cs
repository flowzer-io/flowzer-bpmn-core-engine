using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

/// <summary>
/// Holt beim Hochlauf die persistierten Timer zurueck und arbeitet ueberfaellige Faelligkeiten ab.
/// Frueher lief das synchron nach <c>app.Run()</c>-Vorbereitung im Startpfad: ein blockierender
/// Aufruf auf einer asynchronen Kette, der bei traeger Ablage den Start anhielt und Ausnahmen erst
/// nach dem Binden der Ports zeigte. Als gehosteter Dienst laeuft es im normalen Startvertrag,
/// vor dem <see cref="TimerSchedulerBackgroundService"/>, der danach zyklisch weiterarbeitet.
/// </summary>
public sealed class EngineStartupService(
    BpmnBusinessLogic businessLogic,
    IOptions<TimerSchedulerOptions> timerSchedulerOptions,
    ILogger<EngineStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = timerSchedulerOptions.Value.Enabled;
        logger.LogInformation("Engine-Wiederherstellung startet (Timerautomatik: {Enabled}).", enabled);

        await businessLogic.LoadAsync(enabled, cancellationToken);

        logger.LogInformation("Engine-Wiederherstellung abgeschlossen.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
