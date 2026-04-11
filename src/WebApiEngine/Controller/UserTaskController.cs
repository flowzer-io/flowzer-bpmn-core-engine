using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic) : FlowzerControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>> GetAllUserTasks()
    {
        //Todo nur für den user

        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty);
        var dtos = userTaskSubscriptions.Select(subscription => subscription.ToDto()).ToArray();
        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos));
    }



    [HttpPost]
    public async Task<ActionResult<ApiStatusResult>> HandleUserTaskResult([FromBody] UserTaskResultDto messageDto)
    {

        var userTaskResult = messageDto.ToModel();
        await bpmnBusinessLogic.HandleUserTask(userTaskResult, new Guid()); //TODO: implement authentication

        return Ok();
    }
}
