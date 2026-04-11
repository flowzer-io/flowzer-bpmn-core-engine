using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using WebApiEngine.Auth;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor) : FlowzerControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>> GetAllUserTasks()
    {
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(currentUser.UserId);
        var dtos = userTaskSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos));
    }



    [HttpPost]
    public async Task<ActionResult<ApiStatusResult>> HandleUserTaskResult([FromBody] UserTaskResultDto messageDto)
    {
        var userTaskResult = messageDto.ToModel();
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        await bpmnBusinessLogic.HandleUserTask(userTaskResult, currentUser.UserId);

        return Ok(new ApiStatusResult { Successful = true });
    }
}
