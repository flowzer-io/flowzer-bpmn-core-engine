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
        return Ok(await MapInstances(instances));
    }

    private async Task<ApiStatusResult<List<ProcessInstanceInfoDto>>> MapInstances(IEnumerable<ProcessInstanceInfo> instances)
    {
        var data = await Task.WhenAll(instances.Select(MapInstance));
        var apiStatusResult = new ApiStatusResult<List<ProcessInstanceInfoDto>>(data.ToList());
        return apiStatusResult;
    }

    private async Task<ProcessInstanceInfoDto> MapInstance(ProcessInstanceInfo processInstanceInfo)
    {
        return new ProcessInstanceInfoDto()
        {
            InstanceId = processInstanceInfo.InstanceId,
            DefinitionId = processInstanceInfo.DefinitionId,
            RelatedDefinitionId = processInstanceInfo.metaDefinitionId,
            RelatedDefinitionName =
                (await storageSystem.DefinitionStorage.GetMetaDefinitionById(processInstanceInfo.metaDefinitionId))
                .Name,
            MessageSubscriptionCount = processInstanceInfo.MessageSubscriptionCount,
            SignalSubscriptionCount = processInstanceInfo.SignalSubscriptionCount,
            UserTaskSubscriptionCount = processInstanceInfo.UserTaskSubscriptionCount,
            ServiceSubscriptionCount = processInstanceInfo.ServiceSubscriptionCount,
            State = (ProcessInstanceStateDto)processInstanceInfo.State,
            Tokens = processInstanceInfo.Tokens.Select(token => token.ToDto()).ToList(),
        };
    }



    [HttpGet("{instanceId}")]
    public async Task<ActionResult<ProcessInstanceInfoDto>> GetInstanceById(Guid instanceId)
    {
        var instance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var mapInstance = await MapInstance(instance);
        return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(mapInstance));
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
