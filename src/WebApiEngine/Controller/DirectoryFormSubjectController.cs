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
    ICurrentUserContextAccessor currentUserAccessor,
    DirectorySubjectResolutionContext resolutionContext) : ControllerBase
{
    [HttpGet("user-tasks/{taskId:guid}/assignees")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchTaskAssignees(
        Guid taskId,
        [FromQuery] string action,
        [FromQuery] string? query,
        [FromQuery] int limit = 20)
    {
        if (action is not ("assign" or "delegate"))
            return InvalidSearch("The action must be assign or delegate.");
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length is < 2 or > 100)
            return InvalidSearch("The query must contain between 2 and 100 non-whitespace characters.");
        if (limit is < 1 or > 50) return InvalidSearch("The limit must be between 1 and 50.");

        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("searching user-task assignees");
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task is null) return HiddenNotFound();
        var canOperate = (await authorization.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;
        DirectorySnapshot? snapshot;
        try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        var access = await UserTaskWorkAuthorization.EvaluateAsync(storage, task, currentUser, canOperate, snapshot);
        if (action == "assign" ? !access.CanAssign : !access.CanDelegate) return HiddenNotFound();
        if (snapshot is null) return DirectoryUnavailable();

        var normalizedQuery = query.Trim();
        var users = snapshot.Users
            .Where(user => user.IsActive
                           && (user.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                               || user.Subject.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            .Where(user => action == "assign"
                           || IsCandidate(task, user, snapshot))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Subject, StringComparer.Ordinal)
            .Take(limit)
            .Select(user => new DirectorySubjectDto
            {
                Subject = new SubjectRefDto { Kind = "user", Id = user.Id },
                DisplayName = user.DisplayName,
                Detail = user.Subject,
                IsActive = user.IsActive,
                IsSelectable = user.IsActive
            })
            .ToList();
        return Ok(new ApiStatusResult<DirectorySubjectSearchResultDto>(new DirectorySubjectSearchResultDto
        {
            GenerationId = snapshot.GenerationId,
            Items = users
        }));
    }

    /// <summary>
    /// Löst bereits gespeicherte Bearbeiterreferenzen im Rechtekontext einer konkreten
    /// Lifecycle-Aktion auf. Inaktive oder nicht mehr kandidierende Personen bleiben
    /// beschriftbar, werden aber niemals wieder auswählbar.
    /// </summary>
    [HttpPost("user-tasks/{taskId:guid}/assignees/resolve")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectResolutionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>>> ResolveTaskAssignees(
        Guid taskId,
        [FromQuery] string action,
        [FromBody] DirectorySubjectResolutionRequestDto request)
    {
        if (action is not ("assign" or "delegate"))
            return InvalidResolution("The action must be assign or delegate.");
        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("resolving user-task assignees");
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task is null) return HiddenNotFound();
        var canOperate = (await authorization.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;
        DirectorySnapshot? snapshot;
        try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        var access = await UserTaskWorkAuthorization.EvaluateAsync(storage, task, currentUser, canOperate, snapshot);
        if (action == "assign" ? !access.CanAssign : !access.CanDelegate) return HiddenNotFound();
        if (snapshot is null) return DirectoryUnavailable();
        if (!IdentityDirectorySubjectController.TryParseSubjects(request, out var subjects, out var error))
            return InvalidResolution(error);

        var policy = new DirectorySubjectSelectionPolicy
        {
            AllowUsers = true,
            ActiveOnly = true,
            AllowedUserIds = action == "delegate"
                ? snapshot.Users.Where(user => IsCandidate(task, user, snapshot)).Select(user => user.Id).ToHashSet()
                : null
        };
        var contextSubjects = await resolutionContext.LoadTaskAssigneeSubjects(task, access.State);
        var result = DirectorySubjectSelectionService.ResolveForDisplay(
            snapshot, subjects, policy, contextSubjects);
        return ResolutionResult(result);
    }

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

    /// <summary>Historische Anzeigeauflösung im gebundenen Startformularfeld.</summary>
    [HttpPost("start-forms/{definitionId}/fields/{fieldKey}/subjects/resolve")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectResolutionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>>> ResolveStartForm(
        string definitionId,
        string fieldKey,
        [FromBody] DirectorySubjectResolutionRequestDto request)
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

        return await ResolveBoundField(resolved.Form, fieldKey, request, contextSubjects: null);
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

        var canOperate = (await authorization.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;
        StorageSystem.DirectorySnapshot? snapshot;
        try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        var access = await UserTaskWorkAuthorization.EvaluateAsync(
            storage, task, currentUser, canOperate, snapshot);
        if (!access.CanWork)
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

    /// <summary>Historische Anzeigeauflösung im sichtbaren, gebundenen Aufgabenformularfeld.</summary>
    [HttpPost("user-tasks/{taskId:guid}/fields/{fieldKey}/subjects/resolve")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectResolutionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>>> ResolveTaskForm(
        Guid taskId,
        string fieldKey,
        [FromBody] DirectorySubjectResolutionRequestDto request)
    {
        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("resolving directory subjects for a user task");
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task is null) return HiddenNotFound();

        var canOperate = (await authorization.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded;
        DirectorySnapshot? snapshot;
        try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        var access = await UserTaskWorkAuthorization.EvaluateAsync(
            storage, task, currentUser, canOperate, snapshot);
        if (!access.CanWork) return HiddenNotFound();

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
        var contextSubjects = await resolutionContext.LoadTaskFormSubjects(task, fieldKey);
        return await ResolveBoundField(resolved.Form, fieldKey, request, snapshot, contextSubjects);
    }

    private async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchBoundField(
        FormDto? form,
        string fieldKey,
        string? query,
        string kind,
        int limit,
        StorageSystem.DirectorySnapshot? snapshot = null)
    {
        if (!TryGetBoundField(form, fieldKey, out var field)) return HiddenNotFound();
        var policy = field.SubjectSelection!;

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
            return DirectoryUnavailable();
        }

        var result = DirectorySubjectSelectionService.Search(
            snapshot, query, requestedKind, limit, policy);
        return Ok(new ApiStatusResult<DirectorySubjectSearchResultDto>(new DirectorySubjectSearchResultDto
        {
            GenerationId = result.GenerationId,
            Items = result.Items.Select(ToDto).ToList()
        }));
    }

    private async Task<ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>>> ResolveBoundField(
        FormDto? form,
        string fieldKey,
        DirectorySubjectResolutionRequestDto request,
        DirectorySnapshot? snapshot = null,
        IReadOnlySet<SubjectRef>? contextSubjects = null)
    {
        if (!TryGetBoundField(form, fieldKey, out var field)) return HiddenNotFound();
        if (!IdentityDirectorySubjectController.TryParseSubjects(request, out var subjects, out var error))
            return InvalidResolution(error);

        try { snapshot ??= await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { snapshot = null; }
        if (snapshot is null) return DirectoryUnavailable();

        var permitted = DirectorySubjectResolutionContext.PolicySubjects(field.SubjectSelection!);
        if (contextSubjects is not null) permitted.UnionWith(contextSubjects);
        return ResolutionResult(DirectorySubjectSelectionService.ResolveForDisplay(
            snapshot, subjects, field.SubjectSelection!, permitted));
    }

    private static bool TryGetBoundField(
        FormDto? form,
        string fieldKey,
        out FormField field)
    {
        field = null!;
        if (form?.FormData is null || string.IsNullOrWhiteSpace(fieldKey)) return false;
        FormContract contract;
        try { contract = FormContractCompiler.Compile(form.FormData); }
        catch (InvalidOperationException) { return false; }
        if (!FormContract.IsSupportedProfile(form.ValidationProfile)
            || form.ValidationProfile is not null && form.ValidationProfile != contract.ValidationProfile)
            return false;
        var matches = contract.Fields
            .Where(field => field.Key == fieldKey && field.SubjectSelection is not null)
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return false;
        field = matches[0];
        return true;
    }

    private ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>> ResolutionResult(
        DirectorySubjectResolutionResult result) =>
        Ok(new ApiStatusResult<DirectorySubjectResolutionResultDto>(new DirectorySubjectResolutionResultDto
        {
            GenerationId = result.GenerationId,
            Items = result.Items.Select(ToDto).ToList()
        }));

    private ObjectResult DirectoryUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Identity directory unavailable",
        detail: "No successfully synchronized identity-directory snapshot is available.");

    private ObjectResult HiddenNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Directory form field unavailable",
        detail: "The form field or its resource is not available.");

    private ObjectResult InvalidSearch(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid identity directory search",
        detail: detail);

    private ObjectResult InvalidResolution(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid identity directory resolution",
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

    private static bool IsCandidate(UserTaskSubscription task, DirectoryUser user, DirectorySnapshot snapshot)
    {
        var candidate = new CurrentUserContext(Guid.Empty, "directory", IsFallback: false)
        {
            Identity = new Model.AuthenticatedSubject(user.Issuer, user.Subject)
        };
        return UserTaskAssignment.IsVisibleTo(task, candidate, snapshot, seeAll: false);
    }

    private static DirectorySubjectDto ToDto(DirectorySubjectResult item) => new()
    {
        Subject = new SubjectRefDto
        {
            Kind = item.Subject.Kind == Model.DirectorySubjectKind.User ? "user" : "group",
            Id = item.Subject.Id
        },
        DisplayName = item.DisplayName,
        Detail = item.Detail,
        IsActive = item.IsActive,
        IsSelectable = item.IsSelectable
    };
}
