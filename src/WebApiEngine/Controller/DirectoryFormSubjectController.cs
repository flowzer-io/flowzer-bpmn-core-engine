using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StorageSystem.Exceptions;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Kontextgebundene Suche fuer publizierte Formularfelder. Der Client nennt nur Feld, Suchtext
/// und gewuenschte Art; alle fachlichen Filter stammen aus dem gebundenen Formularstand.
/// </summary>
[ApiController, Route("identity-directory")]
public sealed class DirectoryFormSubjectController(
    IStorageSystem storage,
    BpmnBusinessLogic businessLogic,
    FormKeyResolver forms,
    IAuthorizationService authorization,
    ICurrentUserContextAccessor currentUserAccessor) : ControllerBase
{
    [HttpGet("start-forms/{definitionId}/fields/{fieldKey}/subjects")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchStartForm(
        string definitionId,
        string fieldKey,
        [FromQuery] string? query,
        [FromQuery] string kind = "all",
        [FromQuery] int limit = 20)
    {
        FormKeyResolver.Result resolved;
        try
        {
            var reference = await businessLogic.GetStartFormReference(definitionId);
            if (reference.FormKey is null) return HiddenNotFound();
            resolved = await forms.ResolveAsync(reference.FormKey, reference.DefinitionId);
        }
        catch (Exception exception) when (exception is DefinitionStorageNotFoundException
                                                   or FileNotFoundException
                                                   or InvalidOperationException)
        {
            return HiddenNotFound();
        }

        return await SearchBoundField(resolved.Form, fieldKey, query, kind, limit);
    }

    [HttpGet("user-tasks/{taskId:guid}/fields/{fieldKey}/subjects")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchTaskForm(
        Guid taskId,
        string fieldKey,
        [FromQuery] string? query,
        [FromQuery] string kind = "all",
        [FromQuery] int limit = 20)
    {
        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("searching directory subjects for a user task");
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task is null) return HiddenNotFound();

        UserTaskAssignment.EnsureAssignmentFromModel(task);
        var canOperate = (await authorization.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;
        StorageSystem.DirectorySnapshot? snapshot;
        try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        if (!UserTaskAssignment.IsVisibleTo(task, currentUser, snapshot, canOperate))
            return HiddenNotFound();

        FormKeyResolver.Result resolved;
        try
        {
            var key = (task.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation;
            resolved = await forms.ResolveAsync(key, task.DefinitionId);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException)
        {
            return HiddenNotFound();
        }
        return await SearchBoundField(resolved.Form, fieldKey, query, kind, limit, snapshot);
    }

    private async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchBoundField(
        FormDto? form,
        string fieldKey,
        string? query,
        string kind,
        int limit,
        StorageSystem.DirectorySnapshot? snapshot = null)
    {
        if (form?.FormData is null || string.IsNullOrWhiteSpace(fieldKey)) return HiddenNotFound();
        FormContract contract;
        try { contract = FormContractCompiler.Compile(form.FormData); }
        catch (InvalidOperationException) { return HiddenNotFound(); }
        if (!FormContract.IsSupportedProfile(form.ValidationProfile)
            || form.ValidationProfile is not null && form.ValidationProfile != contract.ValidationProfile)
            return HiddenNotFound();
        var matches = contract.Fields
            .Where(field => field.Key == fieldKey && field.SubjectSelection is not null)
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return HiddenNotFound();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length is < 2 or > 100)
            return InvalidSearch("The query must contain between 2 and 100 non-whitespace characters.");
        if (!TryParseKind(kind, out var requestedKind))
            return InvalidSearch("The kind must be one of: all, user, group.");
        if (limit is < 1 or > 50)
            return InvalidSearch("The limit must be between 1 and 50.");

        try { snapshot ??= await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        if (snapshot is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Identity directory unavailable",
                detail: "No successfully synchronized identity-directory snapshot is available.");
        }

        var result = DirectorySubjectSelectionService.Search(
            snapshot, query, requestedKind, limit, matches[0].SubjectSelection!);
        return Ok(new ApiStatusResult<DirectorySubjectSearchResultDto>(new DirectorySubjectSearchResultDto
        {
            GenerationId = result.GenerationId,
            Items = result.Items.Select(ToDto).ToList()
        }));
    }

    private ObjectResult HiddenNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Directory form field unavailable",
        detail: "The form field or its resource is not available.");

    private ObjectResult InvalidSearch(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid identity directory search",
        detail: detail);

    private static bool TryParseKind(string? value, out DirectorySubjectSearchKind kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            "all" => DirectorySubjectSearchKind.All,
            "user" => DirectorySubjectSearchKind.User,
            "group" => DirectorySubjectSearchKind.Group,
            _ => (DirectorySubjectSearchKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static DirectorySubjectDto ToDto(DirectorySubjectResult item) => new()
    {
        Subject = new SubjectRefDto
        {
            Kind = item.Subject.Kind == Model.DirectorySubjectKind.User ? "user" : "group",
            Id = item.Subject.Id
        },
        DisplayName = item.DisplayName,
        Detail = item.Detail
    };
}
