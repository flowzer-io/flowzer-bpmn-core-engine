using AutoMapper;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    IMapper mapper,
    DefinitionBusinessLogic definitionBusinessLogic,
    BpmnLogic bpmnLogic) : FlowzerControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<UserTaskSubscriptionDto[]>>> GetAllUserTasks()
    {
        //Todo nur für den user
        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasks(Guid.Empty);
        var dtos = mapper.Map<UserTaskSubscriptionDto[]>(userTaskSubscriptions);
        return Ok(new ApiStatusResult<UserTaskSubscriptionDto[]>(dtos));
    }
}