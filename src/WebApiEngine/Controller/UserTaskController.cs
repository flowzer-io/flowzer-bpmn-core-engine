using Microsoft.AspNetCore.Authorization;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using WebApiEngine.Auth;

namespace WebApiEngine.Controller;


[ApiController, Route("[controller]")]
public class UserTaskController(
    IStorageSystem storageSystem,
    UserTaskCompletionService completionService,
    FormKeyResolver formKeyResolver,
    UserTaskViewService taskView,
    IAuthorizationService authorizationService,
    ICurrentUserContextAccessor currentUserContextAccessor) : FlowzerControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>> GetAllUserTasks()
    {
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var userId = currentUser.RequireResolvedUserId("reading user tasks");
        var userTaskSubscriptions = await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(userId);

        // Die Ablage kennt nur die technische Id; die Zuweisungen im Modell nennen Namen und
        // Gruppen. Gefiltert wird deshalb hier, wo der vollstaendige Benutzerkontext vorliegt.
        var identity = new UserTaskIdentity(currentUser.Names, currentUser.Groups);
        var seeAll = await HasOperatorRole();

        var visible = userTaskSubscriptions
            .Select(subscription =>
            {
                UserTaskAssignment.EnsureAssignmentFromModel(subscription);
                return subscription;
            })
            .Where(subscription => UserTaskAssignment.IsVisibleTo(subscription, identity, seeAll));
        // Sequenziell: Der Storage-Vertrag garantiert keine parallel nutzbare DB-Connection.
        var dtos = new List<ExtendedUserTaskSubscriptionDto>();
        foreach (var task in visible) dtos.Add(await taskView.ProjectAsync(task, seeAll));

        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos.ToArray()));
    }

    private async Task<bool> HasOperatorRole() =>
        (await authorizationService.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;

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

        // Eine Aufgabe, die dieser Person nicht zusteht, wird wie eine unbekannte behandelt;
        // ein eigener Fehlercode wuerde ihre Existenz verraten.
        if (subscription is not null)
        {
            UserTaskAssignment.EnsureAssignmentFromModel(subscription);
        }

        if (subscription is not null
            && !UserTaskAssignment.IsVisibleTo(subscription, new UserTaskIdentity(currentUser.Names, currentUser.Groups), await HasOperatorRole()))
        {
            subscription = null;
        }

        if (subscription is null)
        {
            return NotFound(new ApiStatusResult<FormDto>($"User task {userTaskId} was not found."));
        }

        var formKey = (subscription.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation;

        // Die Version des Workflows entscheidet mit: Ein im Workflow eingebettetes Formular steht
        // in genau diesem Diagramm, nicht im Formularbestand.
        var resolved = await formKeyResolver.ResolveAsync(formKey, subscription.DefinitionId);

        if (resolved.Form is null)
        {
            return BadRequest(new ApiStatusResult<FormDto>(resolved.ErrorMessage));
        }

        return Ok(new ApiStatusResult<FormDto>(resolved.Form));
    }

    [HttpPost]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult>> HandleUserTaskResult(
        [FromBody] UserTaskResultDto messageDto,
        [FromHeader(Name = WebApiEngine.Idempotency.HttpIdempotency.HeaderName)] string? _idempotencyKey = null)
    {
        var outcome = await completionService.CompleteAsync(messageDto.ToModel());
        return outcome == UserTaskCompletionOutcome.Completed
            ? Ok(new ApiStatusResult { Successful = true })
            : NotFound(new ApiStatusResult("The user task was not found."));
    }
}
