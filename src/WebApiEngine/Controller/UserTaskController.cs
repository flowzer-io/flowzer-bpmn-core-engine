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
    UserTaskDraftService draftService,
    UserTaskLifecycleService lifecycleService,
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
        var userTaskSubscriptions = (await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(userId)).ToArray();

        // Die Ablage kennt nur die technische Id; die Zuweisungen im Modell nennen Namen und
        // Gruppen. Gefiltert wird deshalb hier, wo der vollstaendige Benutzerkontext vorliegt.
        var seeAll = await HasOperatorRole();
        // Sequenziell: Der Storage-Vertrag garantiert keine parallel nutzbare DB-Connection.
        var dtos = new List<ExtendedUserTaskSubscriptionDto>();
        foreach (var task in userTaskSubscriptions)
        {
            var access = await UserTaskWorkAuthorization.EvaluateAsync(
                storageSystem, task, currentUser, seeAll);
            if (access.CanSee) dtos.Add(await taskView.ProjectAsync(task, seeAll, access));
        }

        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos.ToArray()));
    }

    private async Task<bool> HasOperatorRole() =>
        (await authorizationService.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;

    /// <summary>
    /// Liefert genau eine sichtbare Aufgabe für Deep Links und eingebettete Oberflächen.
    /// Fremde, erledigte und unbekannte Aufgaben verwenden absichtlich denselben 404-Vertrag.
    /// </summary>
    [HttpGet("{userTaskId:guid}")]
    [ProducesResponseType<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>> GetUserTask(
        [FromRoute] Guid userTaskId)
    {
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("reading a user task");
        var task = await storageSystem.SubscriptionStorage.GetUserTaskExtended(userTaskId);
        if (task is null) return HiddenUserTask();

        var canOperate = await HasOperatorRole();
        var access = await UserTaskWorkAuthorization.EvaluateAsync(
            storageSystem, task, currentUser, canOperate);
        if (!access.CanSee) return HiddenUserTask();

        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto>(
            await taskView.ProjectAsync(task, canOperate, access)));
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

        // Eine Aufgabe, die dieser Person nicht zusteht, wird wie eine unbekannte behandelt;
        // ein eigener Fehlercode wuerde ihre Existenz verraten.
        var seeAll = await HasOperatorRole();
        if (subscription is not null)
        {
            var access = await UserTaskWorkAuthorization.EvaluateAsync(
                storageSystem, subscription, currentUser, seeAll);
            if (!access.CanWork) subscription = null;
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

    /// <summary>Liefert den privaten Entwurf des aktuellen Bearbeiters; Revision 0 bedeutet leer.</summary>
    [HttpGet("{userTaskId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult<UserTaskDraftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskDraftDto>>> GetDraft([FromRoute] Guid userTaskId)
    {
        var draft = await draftService.GetAsync(userTaskId);
        return draft is null
            ? HiddenDraft()
            : Ok(new ApiStatusResult<UserTaskDraftDto>(draft));
    }

    /// <summary>Speichert den vollstaendigen privaten Entwurfsstand per Compare-and-swap.</summary>
    [HttpPut("{userTaskId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult<UserTaskDraftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskDraftDto>>> SaveDraft(
        [FromRoute] Guid userTaskId,
        [FromBody] SaveUserTaskDraftRequestDto request)
    {
        var draft = await draftService.SaveAsync(userTaskId, request);
        return draft is null
            ? HiddenDraft()
            : Ok(new ApiStatusResult<UserTaskDraftDto>(draft));
    }

    /// <summary>Verwirft den privaten Entwurf nur bei noch aktueller Revision.</summary>
    [HttpDelete("{userTaskId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult>> DeleteDraft(
        [FromRoute] Guid userTaskId,
        [FromQuery] long expectedRevision,
        [FromQuery] long? expectedTaskRevision = null)
    {
        return await draftService.DeleteAsync(userTaskId, expectedRevision, expectedTaskRevision)
            ? Ok(new ApiStatusResult { Successful = true })
            : HiddenDraft();
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

    [HttpPost("{userTaskId:guid}/claim")]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskWorkStateDto>>> Claim(
        Guid userTaskId, [FromBody] UserTaskClaimRequestDto request) =>
        Lifecycle(await lifecycleService.ClaimAsync(userTaskId, request.ExpectedRevision));

    [HttpPost("{userTaskId:guid}/release")]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskWorkStateDto>>> Release(
        Guid userTaskId, [FromBody] UserTaskReleaseRequestDto request) =>
        Lifecycle(await lifecycleService.ReleaseAsync(userTaskId, request.ExpectedRevision, request.Reason));

    [HttpPost("{userTaskId:guid}/assign")]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskWorkStateDto>>> Assign(
        Guid userTaskId, [FromBody] UserTaskTransferRequestDto request) =>
        Lifecycle(await lifecycleService.AssignAsync(
            userTaskId, request.ExpectedRevision, request.Assignee, request.Reason));

    [HttpPost("{userTaskId:guid}/delegate")]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<UserTaskWorkStateDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<UserTaskWorkStateDto>>> Delegate(
        Guid userTaskId, [FromBody] UserTaskTransferRequestDto request) =>
        Lifecycle(await lifecycleService.DelegateAsync(
            userTaskId, request.ExpectedRevision, request.Assignee, request.Reason));

    private ActionResult<ApiStatusResult<UserTaskWorkStateDto>> Lifecycle(UserTaskWorkStateDto? state) =>
        state is null
            ? NotFound(new ApiStatusResult<UserTaskWorkStateDto>("The user task was not found."))
            : Ok(new ApiStatusResult<UserTaskWorkStateDto>(state));

    private ObjectResult HiddenDraft() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "User task draft unavailable",
        detail: "The user task or draft operation is not available.");

    private ObjectResult HiddenUserTask() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "User task unavailable",
        detail: "The user task is not available.");
}
