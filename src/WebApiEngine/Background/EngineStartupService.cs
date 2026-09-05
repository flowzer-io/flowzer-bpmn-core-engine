using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

/// <summary>
/// Holt beim Hochlauf die persistierten Timer zurueck und arbeitet ueberfaellige Faelligkeiten ab.
///
/// Das laeuft in <see cref="StartingAsync"/>, nicht in <c>StartAsync</c>: Gehostete Dienste starten
/// in Registrierungsreihenfolge, und der Web-Host ist vor allen eigenen Diensten registriert. In
/// <c>StartAsync</c> waeren die Ports also schon offen, bevor der gespeicherte Zustand
/// wiederhergestellt ist. <c>StartingAsync</c> laeuft vor jedem <c>StartAsync</c> und damit vor dem
/// ersten angenommenen Request; schlaegt die Wiederherstellung fehl, startet der Host gar nicht erst.
/// </summary>
public sealed class EngineStartupService(
    BpmnBusinessLogic businessLogic,
    IOptions<TimerSchedulerOptions> timerSchedulerOptions,
    ILogger<EngineStartupService> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var enabled = timerSchedulerOptions.Value.Enabled;
        logger.LogInformation("Engine-Wiederherstellung startet (Timerautomatik: {Enabled}).", enabled);

        await businessLogic.LoadAsync(enabled, cancellationToken);

        logger.LogInformation("Engine-Wiederherstellung abgeschlossen.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
