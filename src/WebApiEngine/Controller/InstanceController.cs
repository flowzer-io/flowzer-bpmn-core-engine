using BpmnServiceTask = BPMN.Activities.ServiceTask;
using Flowzer.Shared;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class InstanceController(
    IStorageSystem storageSystem) : FlowzerControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProcessInstanceInfoDto>>> GetAllInstances()
    {
        var instances = await storageSystem.InstanceStorage.GetAllInstances();
        var mappedInstances = await instances.ToDtosAsync(storageSystem.DefinitionStorage);
        return Ok(new ApiStatusResult<List<ProcessInstanceInfoDto>>(mappedInstances));
    }



    [HttpGet("{instanceId}")]
    public async Task<ActionResult<ProcessInstanceInfoDto>> GetInstanceById(Guid instanceId)
    {
        var instance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var mappedInstance = await instance.ToDtoAsync(storageSystem.DefinitionStorage);
        return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(mappedInstance));
    }
    
    [HttpGet("{instanceId}/subscription/messages")]
    public async Task<ActionResult<MessageSubscriptionDto[]>> GetMessageSubscriptions(Guid instanceId)
    {
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetMessageSubscription(instanceId);
        var result = messageSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<MessageSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/signals")]
    public async Task<ActionResult<SignalSubscriptionDto[]>> GetSignalSubscriptions(Guid instanceId)
    {
        var signalSubscriptions = await storageSystem.SubscriptionStorage.GetSignalSubscriptions(instanceId);
        var result = signalSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<SignalSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/timers")]
    public async Task<ActionResult<TimerSubscriptionDto[]>> GetTimerSubscriptions(Guid instanceId)
    {
        var timerSubscriptions = await storageSystem.SubscriptionStorage.GetTimerSubscriptions(instanceId);
        var result = timerSubscriptions
            .OrderBy(subscription => subscription.DueAt)
            .Select(subscription => subscription.ToDto())
            .ToArray();
        return Ok(new ApiStatusResult<TimerSubscriptionDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/services")]
    public async Task<ActionResult<TokenDto[]>> GetServiceSubscriptions(Guid instanceId)
    {
        var instance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var result = instance.Tokens
            .Where(token => token.CurrentBaseElement is BpmnServiceTask && token.State == FlowNodeState.Active)
            .Select(token => token.ToDto())
            .ToArray();

        return Ok(new ApiStatusResult<TokenDto[]>(result));
    }

    [HttpGet("{instanceId}/subscription/userTasks")]
    public async Task<ActionResult<TokenDto[]>> GetUserTasksSubscriptions(Guid instanceId)
    {
        var messageSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasks(instanceId);
        var result = messageSubscriptions.Select(x => x.Token.ToDto()).ToArray();
        return Ok(new ApiStatusResult<TokenDto[]>(result));
    }
    

  
}
