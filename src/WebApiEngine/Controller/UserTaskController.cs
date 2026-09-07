using Microsoft.AspNetCore.Authorization;
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

        var dtos = userTaskSubscriptions
            .Select(subscription =>
            {
                UserTaskAssignment.EnsureAssignmentFromModel(subscription);
                return subscription;
            })
            .Where(subscription => UserTaskAssignment.IsVisibleTo(subscription, identity, seeAll))
            .Select(subscription => subscription.ToDto())
            .ToArray();

        return Ok(new ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>(dtos));
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
        var resolved = await userTaskFormResolver.ResolveAsync(formKey, subscription.DefinitionId);

        if (resolved.Form is null)
        {
            return BadRequest(new ApiStatusResult<FormDto>(resolved.ErrorMessage));
        }

        return Ok(new ApiStatusResult<FormDto>(resolved.Form));
    }

    [HttpPost]
    public async Task<ActionResult<ApiStatusResult>> HandleUserTaskResult([FromBody] UserTaskResultDto messageDto)
    {
        var userTaskResult = messageDto.ToModel();
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var userId = currentUser.RequireResolvedUserId("completing user tasks");

        // Eine Aufgabe abzuschliessen ist der eigentliche Eingriff; sie nur zu sehen ist der
        // harmlose Teil. Die Zuweisung muss deshalb hier genauso gelten wie in der Liste, sonst
        // genuegte die Kenntnis von TokenId und FlowNodeId, um fremde Arbeit zu erledigen.
        if (!await MayWorkOnToken(userTaskResult.TokenId, currentUser))
        {
            return NotFound(new ApiStatusResult($"User task for token {userTaskResult.TokenId} was not found."));
        }

        await bpmnBusinessLogic.HandleUserTask(userTaskResult, userId);

        return Ok(new ApiStatusResult { Successful = true });
    }

    /// <summary>
    /// Sucht die Subscription zum Token und prueft die Zuweisung. Ist zu dem Token keine
    /// Subscription bekannt, bleibt es beim bisherigen Verhalten: Die Engine beurteilt den
    /// Vorgang und antwortet mit ihrem eigenen Fehler.
    /// </summary>
    private async Task<bool> MayWorkOnToken(Guid tokenId, CurrentUserContext currentUser)
    {
        var subscriptions = (await storageSystem.SubscriptionStorage.GetAllUserTasksExtended(currentUser.UserId)).ToList();
        var subscription = subscriptions.FirstOrDefault(candidate => candidate.Token?.Id == tokenId);

        if (subscription is null)
        {
            return true;
        }

        UserTaskAssignment.EnsureAssignmentFromModel(subscription);
        return UserTaskAssignment.IsVisibleTo(subscription, new UserTaskIdentity(currentUser.Names, currentUser.Groups), await HasOperatorRole());
    }
}
