using BPMN.Flowzer;
using Flowzer.Shared;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Microsoft.AspNetCore.Authorization;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class InstanceController(
    IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor,
    InstanceAccessService instanceAccess,
    RuntimeDiagramService runtimeDiagramService) : FlowzerControllerBase
{
    private const string MissingInstance = "The process instance was not found.";
    /// <summary>
    /// Bricht eine laufende Instanz ab. Beendete Instanzen antworten mit 409, unbekannte mit 404.
    /// </summary>
    [HttpPost("{instanceId}/cancel")]
    // Ein Abbruch beendet fremde Arbeit; das ist eine Betriebsentscheidung.
    [Authorize(Policy = FlowzerPolicies.Operator)]
    public async Task<ActionResult<ApiStatusResult<ProcessInstanceInfoDto>>> CancelInstance(Guid instanceId)
    {
        currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("cancelling instances");

        try
        {
            var cancelledInstance = await bpmnBusinessLogic.CancelInstance(instanceId);
            var dto = await cancelledInstance.ToDtoAsync(storageSystem.DefinitionStorage);
            return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(dto));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ApiStatusResult<ProcessInstanceInfoDto>(exception.Message));
        }
    }

    /// <summary>
    /// Trockenlauf der Instanzmigration: Quell- und Zielversion sowie je Instanz, ob sie
    /// migrierbar ist und was der Umzug mitnimmt. Veraendert nichts.
    /// </summary>
    [HttpPost("migration/preview")]
    // Ein Versionswechsel greift in fremde Vorgaenge ein; das ist eine Betriebsentscheidung.
    [Authorize(Policy = FlowzerPolicies.Operator)]
    [ProducesResponseType<ApiStatusResult<InstanceMigrationPreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<InstanceMigrationPreviewDto>>> PreviewInstanceMigration(
        [FromBody] InstanceMigrationPreviewRequestDto request)
    {
        currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("migrating instances");

        var preview = await bpmnBusinessLogic.PreviewInstanceMigration(
            request.InstanceIds, request.FlowNodeMapping);
        if (preview.Status != InstanceMigrationRequestStatus.Accepted)
            return MigrationProblem<InstanceMigrationPreviewDto>(preview.Status, preview.Message);

        return Ok(new ApiStatusResult<InstanceMigrationPreviewDto>(
            await preview.ToDtoAsync(storageSystem.DefinitionStorage)));
    }

    /// <summary>
    /// Migriert die genannten Instanzen auf die deployte Version. Ist inzwischen eine andere
    /// Version deployt, antwortet die API mit 409, ohne etwas zu veraendern.
    /// </summary>
    [HttpPost("migration")]
    [Authorize(Policy = FlowzerPolicies.Operator)]
    [ProducesResponseType<ApiStatusResult<InstanceMigrationResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<InstanceMigrationResultDto>>> MigrateInstances(
        [FromBody] InstanceMigrationRequestDto request)
    {
        var user = currentUserContextAccessor.GetCurrentUser();
        user.RequireResolvedUserId("migrating instances");

        var outcome = await bpmnBusinessLogic.MigrateInstances(
            request.InstanceIds, request.TargetDefinitionId, user.UserId, request.FlowNodeMapping);
        if (outcome.Status != InstanceMigrationRequestStatus.Accepted)
            return MigrationProblem<InstanceMigrationResultDto>(outcome.Status, outcome.Message);

        return Ok(new ApiStatusResult<InstanceMigrationResultDto>(outcome.ToDto()));
    }

    /// <summary>
    /// Bildet die Ablehnung einer ganzen Anfrage auf ihren Statuscode ab: Was es nicht gibt,
    /// ist 404, ein ueberholter Zielstand ist 409, alles andere eine unbrauchbare Angabe.
    /// </summary>
    private ActionResult<ApiStatusResult<T>> MigrationProblem<T>(
        InstanceMigrationRequestStatus status,
        string? message)
    {
        var statusCode = status switch
        {
            InstanceMigrationRequestStatus.UnknownInstance => StatusCodes.Status404NotFound,
            InstanceMigrationRequestStatus.TargetVersionChanged => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Problem(
            statusCode: statusCode,
            title: statusCode switch
            {
                StatusCodes.Status404NotFound => "Process instance not found",
                StatusCodes.Status409Conflict => "The deployed version has changed",
                _ => "The migration request could not be processed"
            },
            detail: message);
    }

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<List<ProcessInstanceInfoDto>>>> GetAllInstances()
    {
        var mappedInstances = await instanceAccess.GetAllAsync();
        return Ok(new ApiStatusResult<List<ProcessInstanceInfoDto>>(mappedInstances));
    }



    [HttpGet("{instanceId}")]
    public async Task<ActionResult<ApiStatusResult<ProcessInstanceInfoDto>>> GetInstanceById(Guid instanceId)
    {
        var mappedInstance = await instanceAccess.GetAsync(instanceId);
        if (mappedInstance is null) return NotFound(new ApiStatusResult<ProcessInstanceInfoDto>(MissingInstance));
        return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(mappedInstance));
    }

    /// <summary>
    /// Liefert die append-only gespeicherten Human-Task-Aktionen einer sichtbaren
    /// Instanz. Die Projektion enthält bewusst keine Personen- oder Formulardaten.
    /// </summary>
    [HttpGet("{instanceId}/history")]
    [ProducesResponseType<ApiStatusResult<ProcessHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<ProcessHistoryDto>>> GetHistory(Guid instanceId)
    {
        if (await instanceAccess.GetAsync(instanceId) is null)
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Process instance not found",
                detail: MissingInstance);

        var events = (await storageSystem.UserTaskLifecycleStorage
                .GetEventsByProcessInstance(instanceId))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.UserTaskId)
            .ThenBy(item => item.Revision)
            .ThenBy(item => item.Id)
            .Select(item => item.ToHistoryDto())
            .ToArray();
        return Ok(new ApiStatusResult<ProcessHistoryDto>(new ProcessHistoryDto
        {
            InstanceId = instanceId,
            Events = events
        }));
    }

    /// <summary>
    /// Liefert die bereinigte BPMN-Struktur und die append-only Engine-Ereignisspur der
    /// exakt an die Instanz gebundenen Version. Dieser Diagnoseweg ist nur für den Betrieb.
    /// </summary>
    [HttpGet("{instanceId}/runtime-diagram")]
    [ProducesResponseType<ApiStatusResult<RuntimeDiagramDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<RuntimeDiagramDto>>> GetRuntimeDiagram(Guid instanceId)
    {
        var diagram = await runtimeDiagramService.GetAsync(instanceId);
        if (diagram is null)
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Process instance not found",
                detail: MissingInstance);

        return Ok(new ApiStatusResult<RuntimeDiagramDto>(diagram));
    }
    
    [HttpGet("{instanceId}/subscription/messages")]
    public async Task<ActionResult<ApiStatusResult<MessageSubscriptionDto[]>>> GetMessageSubscriptions(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return NotFound(new ApiStatusResult<MessageSubscriptionDto[]>(MissingInstance));
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetMessageSubscription(instanceId);
        var result = messageSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<MessageSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/signals")]
    public async Task<ActionResult<ApiStatusResult<SignalSubscriptionDto[]>>> GetSignalSubscriptions(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return NotFound(new ApiStatusResult<SignalSubscriptionDto[]>(MissingInstance));
        var signalSubscriptions = await storageSystem.SubscriptionStorage.GetSignalSubscriptions(instanceId);
        var result = signalSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<SignalSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/timers")]
    public async Task<ActionResult<ApiStatusResult<TimerSubscriptionDto[]>>> GetTimerSubscriptions(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return NotFound(new ApiStatusResult<TimerSubscriptionDto[]>(MissingInstance));
        var timerSubscriptions = await storageSystem.SubscriptionStorage.GetTimerSubscriptions(instanceId);
        var result = timerSubscriptions
            .OrderBy(subscription => subscription.DueAt)
            .Select(subscription => subscription.ToDto())
            .ToArray();
        return Ok(new ApiStatusResult<TimerSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/services")]
    public async Task<ActionResult<ApiStatusResult<TokenDto[]>>> GetServiceSubscriptions(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return NotFound(new ApiStatusResult<TokenDto[]>(MissingInstance));
        var instance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var result = instance.Tokens
            // Dieselbe Menge, aus der Auftraege entstehen: seit Vertrag 6 auch Send-Tasks und
            // sendende Nachrichtenereignisse mit Auftragstyp.
            .Where(token => token.State == FlowNodeState.Active
                && token.CurrentBaseElement is IFlowzerWorkerTask { Implementation.Length: > 0 })
            .Select(token => token.ToDto())
            .ToArray();

        return Ok(new ApiStatusResult<TokenDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/userTasks")]
    public async Task<ActionResult<ApiStatusResult<TokenDto[]>>> GetUserTasksSubscriptions(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return NotFound(new ApiStatusResult<TokenDto[]>(MissingInstance));
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasks(instanceId);
        var result = messageSubscriptions.Select(x => x.Token.ToDto()).ToArray();
        return Ok(new ApiStatusResult<TokenDto[]>(result));
    }
    

  
}
