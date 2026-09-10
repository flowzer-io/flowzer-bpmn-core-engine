using BpmnServiceTask = BPMN.Activities.ServiceTask;
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
    InstanceAccessService instanceAccess) : FlowzerControllerBase
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
            .Where(token => token.CurrentBaseElement is BpmnServiceTask && token.State == FlowNodeState.Active)
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
