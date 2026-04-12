using System.Diagnostics;
using Model;
using WebApiEngine.Diagnostics;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("operations")]
public class OperationsController(
    IStorageSystem storageSystem,
    IHostEnvironment environment,
    TimerSchedulerDiagnosticsState timerSchedulerDiagnosticsState,
    ILogger<OperationsController> logger) : ControllerBase
{
    [HttpGet("diagnostics")]
    public async Task<ActionResult<ApiStatusResult<OperationsDiagnosticsDto>>> GetDiagnostics()
    {
        using var activity = FlowzerDiagnostics.ActivitySource.StartActivity("operations.diagnostics", ActivityKind.Internal);

        try
        {
            var definitions = (await storageSystem.DefinitionStorage.GetAllDefinitions()).ToArray();
            var metaDefinitions = (await storageSystem.DefinitionStorage.GetAllMetaDefinitions()).ToArray();
            var forms = (await storageSystem.FormStorage.GetFormMetadatas()).ToArray();
            var instances = (await storageSystem.InstanceStorage.GetAllInstances()).ToArray();
            var activeInstances = (await storageSystem.InstanceStorage.GetAllActiveInstances()).ToArray();
            var messages = (await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions()).ToArray();
            var timers = (await storageSystem.SubscriptionStorage.GetAllTimerSubscriptions()).ToArray();

            var payload = new OperationsDiagnosticsDto
            {
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = new OperationsStorageSnapshotDto
                {
                    StorageRootHint = ResolveStorageRootHint(),
                    TotalDefinitions = definitions.Length,
                    ActiveDefinitions = definitions.Count(definition => definition.IsActive),
                    DefinitionMetadataEntries = metaDefinitions.Length,
                    FormMetadataEntries = forms.Length,
                    TotalInstances = instances.Length,
                    ActiveInstances = activeInstances.Length,
                    CompletedInstances = instances.Count(instance =>
                        instance.State is ProcessInstanceState.Completed or ProcessInstanceState.Compensated),
                    FailedInstances = instances.Count(instance =>
                        instance.State is ProcessInstanceState.Failed or ProcessInstanceState.Terminated),
                    PendingMessages = messages.Length,
                    PendingTimers = timers.Length,
                    OpenUserTasks = activeInstances.Sum(instance => instance.UserTaskSubscriptionCount),
                    PendingSignals = activeInstances.Sum(instance => instance.SignalSubscriptionCount),
                    PendingServices = activeInstances.Sum(instance => instance.ServiceSubscriptionCount)
                },
                TimerScheduler = timerSchedulerDiagnosticsState.GetSnapshot(),
                Instrumentation = new OperationsInstrumentationDto
                {
                    MeterName = FlowzerDiagnostics.MeterName,
                    ActivitySourceName = FlowzerDiagnostics.ActivitySourceName,
                    Notes =
                        "Dieses Paket liefert bewusst nur die lokale Metrics-/Tracing-Grundlage. Exporter und externe Observability-Backends bleiben Folgearbeit."
                }
            };

            activity?.SetTag("flowzer.instances.total", payload.Storage.TotalInstances);
            activity?.SetTag("flowzer.subscriptions.timers", payload.Storage.PendingTimers);

            return Ok(new ApiStatusResult<OperationsDiagnosticsDto>(payload));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not build operations diagnostics snapshot.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiStatusResult<OperationsDiagnosticsDto>
            {
                Successful = false,
                ErrorMessage = "Operations diagnostics are currently unavailable."
            });
        }
    }

    private static string ResolveStorageRootHint()
    {
        var configuredStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
        return string.IsNullOrWhiteSpace(configuredStorageRoot)
            ? "(default under FilesystemStorageSystem build output)"
            : Path.GetFullPath(configuredStorageRoot);
    }
}
