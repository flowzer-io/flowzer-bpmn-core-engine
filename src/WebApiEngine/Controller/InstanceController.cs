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
    ICurrentUserContextAccessor currentUserContextAccessor) : FlowzerControllerBase
{
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
        var instances = await storageSystem.InstanceStorage.GetAllInstances();
        var mappedInstances = await instances.ToDtosAsync(storageSystem.DefinitionStorage);
        return Ok(new ApiStatusResult<List<ProcessInstanceInfoDto>>(mappedInstances));
    }



    [HttpGet("{instanceId}")]
    public async Task<ActionResult<ApiStatusResult<ProcessInstanceInfoDto>>> GetInstanceById(Guid instanceId)
    {
        var instance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var mappedInstance = await instance.ToDtoAsync(storageSystem.DefinitionStorage);
        return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(mappedInstance));
    }
    
    [HttpGet("{instanceId}/subscription/messages")]
    public async Task<ActionResult<ApiStatusResult<MessageSubscriptionDto[]>>> GetMessageSubscriptions(Guid instanceId)
    {
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetMessageSubscription(instanceId);
        var result = messageSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<MessageSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/signals")]
    public async Task<ActionResult<ApiStatusResult<SignalSubscriptionDto[]>>> GetSignalSubscriptions(Guid instanceId)
    {
        var signalSubscriptions = await storageSystem.SubscriptionStorage.GetSignalSubscriptions(instanceId);
        var result = signalSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<SignalSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/timers")]
    public async Task<ActionResult<ApiStatusResult<TimerSubscriptionDto[]>>> GetTimerSubscriptions(Guid instanceId)
    {
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
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasks(instanceId);
        var result = messageSubscriptions.Select(x => x.Token.ToDto()).ToArray();
        return Ok(new ApiStatusResult<TokenDto[]>(result));
    }
    

  
}
