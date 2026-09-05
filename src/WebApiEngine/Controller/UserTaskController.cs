using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using WebApiEngine.Auth;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic,
    UserTaskFormResolver userTaskFormResolver,
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

    /// <summary>
    /// Liefert das Formular, das zu einem offenen User-Task gehört.
    /// Fasst die bisher clientseitige Auflösung (Form-Key lesen, Metadaten suchen,
    /// Version laden) zu einem einzigen Aufruf zusammen.
    /// </summary>
    [HttpGet("{userTaskId:guid}/form")]
    public async Task<ActionResult<ApiStatusResult<FormDto>>> GetUserTaskForm([FromRoute] Guid userTaskId)
    {
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var userId = currentUser.RequireResolvedUserId("reading user tasks");

        // Einzelzugriff statt aller Aufgaben aller Personen: der Aufwand haengt sonst am Gesamtbestand.
        var subscription = await storageSystem.SubscriptionStorage.GetUserTaskExtended(userTaskId);

        if (subscription is null)
        {
            return NotFound(new ApiStatusResult<FormDto>($"User task {userTaskId} was not found."));
        }

        var formKey = (subscription.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation;
        var resolved = await userTaskFormResolver.ResolveAsync(formKey);

        if (resolved.Form is null)
        {
            return BadRequest(new ApiStatusResult<FormDto>(resolved.ErrorMessage));
        }

        return Ok(new ApiStatusResult<FormDto>(resolved.Form.ToDto()));
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
