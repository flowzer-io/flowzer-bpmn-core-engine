using AutoMapper;
using Flowzer.Shared;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class InstanceController(
    IStorageSystem storageSystem,
    IMapper mapper,
    DefinitionBusinessLogic definitionBusinessLogic,
    BpmnLogic bpmnLogic) : FlowzerControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProcessInstanceInfoDto>>> GetAllRunningInstances()
    {
        var instances = await storageSystem.InstanceStorage.GetAllActiveInstances();
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
            RelatedDefinitionId = processInstanceInfo.RelatedDefinitionId,
            RelatedDefinitionName =
                (await storageSystem.DefinitionStorage.GetMetaDefinitionById(processInstanceInfo.RelatedDefinitionId))
                .Name,
            MessageSubscriptionCount = processInstanceInfo.MessageSubscriptionCount,
            SignalSubscriptionCount = processInstanceInfo.SignalSubscriptionCount,
            UserTaskSubscriptionCount = processInstanceInfo.UserTaskSubscriptionCount,
            ServiceSubscriptionCount = processInstanceInfo.ServiceSubscriptionCount,
            State = (ProcessInstanceStateDto)processInstanceInfo.State,
            Tokens = processInstanceInfo.Tokens.Select(mapper.Map<TokenDto>).ToList(),
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
        var result = mapper.Map<MessageSubscriptionDto[]>(messageSubscriptions);
        return Ok(new ApiStatusResult<MessageSubscriptionDto[]>(result));
    }
    

  
}
