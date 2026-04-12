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
        var userId = currentUser.RequireResolvedUserId("reading user tasks");
        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(userId);
        var dtos = userTaskSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos));
    }



    [HttpPost]
    public async Task<ActionResult<ApiStatusResult>> HandleUserTaskResult([FromBody] UserTaskResultDto messageDto)
    {
        var userTaskResult = messageDto.ToModel();
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var userId = currentUser.RequireResolvedUserId("completing user tasks");
        await bpmnBusinessLogic.HandleUserTask(userTaskResult, userId);

        return Ok(new ApiStatusResult { Successful = true });
    }
}
