using System.Diagnostics;
using Microsoft.Extensions.Options;
using Model;
using WebApiEngine.Diagnostics;
using WebApiEngine.Shared;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;

namespace WebApiEngine.Controller;

[ApiController, Route("operations")]
// Diagnose zeigt Ablageort, Zaehler und Zustaende des Hosts; das gehoert dem Betrieb.
[Authorize(Policy = FlowzerPolicies.Operator)]
public class OperationsController(
    IStorageSystem storageSystem,
    IHostEnvironment environment,
    TimerSchedulerDiagnosticsState timerSchedulerDiagnosticsState,
    IOptions<FlowzerObservabilityOptions> observabilityOptions,
    WebApiEngine.Persistence.FlowzerStorageOptions storageOptions,
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
            var stalledJobs = (await storageSystem.ServiceTaskStorage.GetJobs()).Count(IsStalled);

            var payload = new OperationsDiagnosticsDto
            {
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = new OperationsStorageSnapshotDto
                {
                    StorageRootHint = storageOptions.IsPostgreSql ? storageOptions.Describe() : ResolveStorageRootHint(environment),
                    TotalDefinitions = definitions.Length,
                    ActiveDefinitions = definitions.Count(definition => definition.IsActive),
                    DefinitionMetadataEntries = metaDefinitions.Length,
                    FormMetadataEntries = forms.Length,
                    TotalInstances = instances.Length,
                    ActiveInstances = activeInstances.Length,
                    CompletedInstances = instances.Count(instance =>
                        instance.State is ProcessInstanceState.Completed or ProcessInstanceState.Compensated),
                    FailedInstances = instances.Count(instance => instance.State is ProcessInstanceState.Failed),
                    // Ein Abbruch ist kein Fehler: Er entsteht durch die Betriebsaktion „Instanz abbrechen“
                    // oder durch ein Terminate-Endereignis (etwa beim abgelehnten Urlaubsantrag) und ist damit
                    // ein regulaerer Ausgang. Die Konsole zeigt ihn als „Abgebrochen“; das Betriebsbild muss
                    // dieselbe Aussage treffen. Die drei Endzustaende ergaenzen zusammen mit ActiveInstances
                    // genau TotalInstances — die Zwischenzustaende (Completing, Failing, Terminating,
                    // Compensating) erreicht die Ablage laut InstanceEngine.State nie, faellt aber trotzdem
                    // einer von ihnen an, zaehlt er als aktiv und geht damit nicht verloren.
                    CancelledInstances = instances.Count(instance => instance.State is ProcessInstanceState.Terminated),
                    PendingMessages = messages.Length,
                    PendingTimers = timers.Length,
                    OpenUserTasks = activeInstances.Sum(instance => instance.UserTaskSubscriptionCount),
                    PendingSignals = activeInstances.Sum(instance => instance.SignalSubscriptionCount),
                    PendingServices = activeInstances.Sum(instance => instance.ServiceSubscriptionCount)
                },
                // Nur die Zaehler, nicht die Liste: Die Betriebsseite zeigt damit die Kachel
                // „Stoerungen", ohne fuer jeden Abruf die Eingaben aller liegen gebliebenen
                // Auftraege mitzuladen.
                Incidents = new OperationsIncidentCountersDto
                {
                    JobExhausted = stalledJobs,
                    InstanceFailed = instances.Count(instance => instance.State is ProcessInstanceState.Failed)
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

    /// <summary>
    /// Alles, was ohne einen Eingriff liegen bleibt — an einer Stelle, neueste Störung zuerst.
    ///
    /// Die Liste wird abgeleitet und nicht geführt: Ein Auftrag ohne verbleibende Versuche und
    /// eine gescheiterte Instanz sind bereits vollständig beschrieben. Eine eigene Tabelle
    /// daneben könnte nur noch veralten und müsste bei jedem Abbruch, jeder Migration und jedem
    /// Neustart mitgepflegt werden.
    ///
    /// KI-Läufe stehen hier bewusst nicht: Sie haben einen eigenen Lauf- und Freigabevertrag.
    /// </summary>
    [HttpGet("incidents")]
    public async Task<ActionResult<ApiStatusResult<OperationsIncidentDto[]>>> GetIncidents()
    {
        using var activity = FlowzerDiagnostics.ActivitySource.StartActivity("operations.incidents", ActivityKind.Internal);

        try
        {
            var metaNames = (await storageSystem.DefinitionStorage.GetAllMetaDefinitions())
                .GroupBy(metaDefinition => metaDefinition.DefinitionId)
                .ToDictionary(group => group.Key, group => group.First().Name);
            var jobs = (await storageSystem.ServiceTaskStorage.GetJobs()).Where(IsStalled);
            var failedInstances = (await storageSystem.InstanceStorage.GetAllInstances())
                .Where(instance => instance.State is ProcessInstanceState.Failed);

            var incidents = jobs.Select(job => ToIncident(job, metaNames))
                .Concat(failedInstances.Select(instance => ToIncident(instance, metaNames)))
                .OrderByDescending(incident => incident.Since)
                .ToArray();

            activity?.SetTag("flowzer.incidents.total", incidents.Length);

            return Ok(new ApiStatusResult<OperationsIncidentDto[]>(incidents));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not build the operations incident list.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiStatusResult<OperationsIncidentDto[]>
            {
                Successful = false,
                ErrorMessage = "The operations incident list is currently unavailable."
            });
        }
    }

    /// <summary>
    /// Ein Auftrag liegt, wenn seine Versuche verbraucht sind. Eine noch laufende Sperre zählt
    /// nicht dagegen: Sie wäre an einem Auftrag ohne Versuche ein Rest aus einem abgebrochenen
    /// Anlauf und darf die Störung nicht verstecken.
    /// </summary>
    private static bool IsStalled(ServiceTaskJob job) => job.Retries <= 0;

    private static OperationsIncidentDto ToIncident(ServiceTaskJob job, IReadOnlyDictionary<string, string> metaNames) =>
        new()
        {
            Kind = OperationsIncidentKinds.JobExhausted,
            InstanceId = job.ProcessInstanceId,
            MetaDefinitionId = job.MetaDefinitionId,
            DefinitionId = job.DefinitionId,
            DefinitionName = metaNames.GetValueOrDefault(job.MetaDefinitionId, job.MetaDefinitionId),
            FlowNodeId = job.FlowNodeId,
            FlowNodeName = string.IsNullOrWhiteSpace(job.Name) ? job.FlowNodeId : job.Name,
            JobId = job.Id,
            JobType = job.Type,
            Message = job.LastErrorMessage,
            Since = job.RetryAt ?? job.CreatedAt,
            ManualRetries = job.RetryHistory.Count,
            Variables = job.Variables is null
                ? []
                : ((IDictionary<string, object?>)job.Variables).ToDictionary(entry => entry.Key, entry => entry.Value)
        };

    private static OperationsIncidentDto ToIncident(
        ProcessInstanceInfo instance,
        IReadOnlyDictionary<string, string> metaNames)
    {
        // Der Knoten, an dem es aufgehört hat. Das Master-Token scheitert mit, trägt aber den
        // Prozess und nicht den Schritt; gesucht ist deshalb das jüngste gescheiterte Token
        // darunter.
        var failedNode = instance.Tokens
            .Where(token => token.State == FlowNodeState.Failed && token.ParentTokenId is not null)
            .OrderByDescending(token => token.LastStateChangeTime)
            .Select(token => token.CurrentFlowNode)
            .FirstOrDefault(flowNode => flowNode is not null);

        return new OperationsIncidentDto
        {
            Kind = OperationsIncidentKinds.InstanceFailed,
            InstanceId = instance.InstanceId,
            MetaDefinitionId = instance.metaDefinitionId,
            DefinitionId = instance.DefinitionId,
            DefinitionName = metaNames.GetValueOrDefault(instance.metaDefinitionId, instance.metaDefinitionId),
            FlowNodeId = failedNode?.Id,
            FlowNodeName = string.IsNullOrWhiteSpace(failedNode?.Name) ? failedNode?.Id : failedNode.Name,
            Message = instance.FailureReason,
            Since = instance.Tokens.Count == 0
                ? DateTime.UtcNow
                : instance.Tokens.Max(token => token.LastStateChangeTime)
        };
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
            OtlpEndpointHint = options.Enabled && options.HasOtlpExporter
                ? RedactOtlpEndpoint(options.OtlpEndpoint)
                : null,
            OtlpProtocol = options.Enabled && options.HasOtlpExporter
                ? options.ResolveOtlpProtocol().ToString()
                : null,
            OtlpHeadersHint = options.Enabled && options.HasOtlpExporter && !string.IsNullOrWhiteSpace(options.OtlpHeaders)
                ? "(configured)"
                : null,
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
