using AutoMapper;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    IMapper mapper,
    DefinitionBusinessLogic definitionBusinessLogic,
    BpmnBusinessLogic bpmnBusinessLogic) : FlowzerControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>> GetAllUserTasks()
    {
        //Todo nur für den user
        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty);
        var dtos = mapper.Map<ExtendedUserTaskSubscriptionDto[]>(userTaskSubscriptions);
        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos));
    }
}