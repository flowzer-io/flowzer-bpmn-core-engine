using System.Diagnostics;
using Microsoft.Extensions.Options;
using Model;
using WebApiEngine.Diagnostics;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("operations")]
public class OperationsController(
    IStorageSystem storageSystem,
    IHostEnvironment environment,
    TimerSchedulerDiagnosticsState timerSchedulerDiagnosticsState,
    IOptions<FlowzerObservabilityOptions> observabilityOptions,
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
            var activeInstances = instances
                .Where(instance => instance.State is not (ProcessInstanceState.Completed or ProcessInstanceState.Compensated or ProcessInstanceState.Failed or ProcessInstanceState.Terminated))
                .ToArray();
            var messages = (await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions()).ToArray();
            var timers = (await storageSystem.SubscriptionStorage.GetAllTimerSubscriptions()).ToArray();

            var payload = new OperationsDiagnosticsDto
            {
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = new OperationsStorageSnapshotDto
                {
                    StorageRootHint = ResolveStorageRootHint(environment),
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
                        "Die lokale Diagnosebasis bleibt klein, kann jetzt aber optional über OpenTelemetry-Exporter nach außen angebunden werden."
                },
                Observability = CreateObservabilitySnapshot(observabilityOptions.Value)
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

    private static string ResolveStorageRootHint(IHostEnvironment environment)
    {
        var configuredStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredStorageRoot))
        {
            return "(default under FilesystemStorageSystem build output)";
        }

        var normalizedStorageRoot = Path.GetFullPath(configuredStorageRoot);
        if (environment.IsDevelopment())
        {
            return normalizedStorageRoot;
        }

        var leafName = Path.GetFileName(normalizedStorageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leafName)
            ? "(custom FLOWZER_STORAGE_ROOT configured)"
            : $"(custom FLOWZER_STORAGE_ROOT configured: {leafName})";
    }

    private static OperationsObservabilityDto CreateObservabilitySnapshot(FlowzerObservabilityOptions options)
    {
        return new OperationsObservabilityDto
        {
            Enabled = options.Enabled,
            ConsoleExporterEnabled = options.Enabled && options.UseConsoleExporter,
            OtlpExporterEnabled = options.Enabled && options.HasOtlpExporter,
            OtlpEndpointHint = RedactOtlpEndpoint(options.OtlpEndpoint),
            OtlpProtocol = options.Enabled && options.HasOtlpExporter
                ? options.ResolveOtlpProtocol().ToString()
                : null,
            OtlpHeadersHint = string.IsNullOrWhiteSpace(options.OtlpHeaders)
                ? null
                : "(configured)",
            ServiceName = options.ServiceName,
            ServiceVersion = options.ResolveServiceVersion()
        };
    }

    private static string? RedactOtlpEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path);
    }
}
