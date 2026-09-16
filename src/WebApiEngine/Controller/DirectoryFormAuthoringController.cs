using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Autorensuche ohne Prozessinstanz. Nur Modellierende dürfen Filter konfigurieren und
/// lokale Formularstände ausprobieren. Die gebundenen Laufzeit-Endpunkte bleiben unverändert.
/// </summary>
[ApiController, Route("identity-directory")]
[Authorize(Policy = FlowzerPolicies.Modeler)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
public sealed class DirectoryFormAuthoringController(IStorageSystem storage, FormAuthoringService authoring) : ControllerBase
{
    [HttpPost("authoring-forms/{formId:guid}/subjects/search")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> Search(Guid formId, DirectoryAuthoringRequestDto request)
    {
        var policy = await LoadPolicy(formId, request);
        if (policy is null) return Problem(statusCode: 404, title: "Form directory context unavailable");
        var kind = request.Kind switch { "all" => DirectorySubjectSearchKind.All, "user" => DirectorySubjectSearchKind.User,
            "group" => DirectorySubjectSearchKind.Group, _ => (DirectorySubjectSearchKind)(-1) };
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length is < 2 or > 100)
            return Problem(statusCode: 400, title: "Invalid directory search");
        var snapshot = await Snapshot();
        if (snapshot is null) return Unavailable();
        var result = DirectorySubjectSelectionService.Search(snapshot, request.Query, kind, 30, policy);
        return Ok(new ApiStatusResult<DirectorySubjectSearchResultDto>(new DirectorySubjectSearchResultDto()
        { GenerationId = result.GenerationId, Items = result.Items.Select(ToDto).ToList() }));
    }

    [HttpPost("authoring-forms/{formId:guid}/subjects/resolve")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectResolutionResultDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectResolutionResultDto>>> Resolve(Guid formId, DirectoryAuthoringRequestDto request)
    {
        var policy = await LoadPolicy(formId, request);
        if (policy is null) return Problem(statusCode: 404, title: "Form directory context unavailable");
        if (!IdentityDirectorySubjectController.TryParseSubjects(new() { Subjects = request.Subjects }, out var subjects, out var error))
            return Problem(statusCode: 400, title: "Invalid directory references", detail: error);
        var snapshot = await Snapshot();
        if (snapshot is null) return Unavailable();
        // Keine historischen/ausgeschlossenen Identitäten allein aufgrund einer Browser-ID offenlegen.
        var items = subjects.Select(subject => DirectorySubjectSelectionService.Resolve(snapshot, subject, policy))
            .OfType<DirectorySubjectResult>().Select(ToDto).ToList();
        return Ok(new ApiStatusResult<DirectorySubjectResolutionResultDto>(new DirectorySubjectResolutionResultDto()
        { GenerationId = snapshot.GenerationId, Items = items }));
    }

    private async Task<DirectorySubjectSelectionPolicy?> LoadPolicy(Guid formId, DirectoryAuthoringRequestDto request)
    {
        try { _ = await storage.FormStorage.GetFormMetaData(formId); }
        catch (Exception error) when (error is FileNotFoundException or InvalidOperationException) { return null; }
        // Die breite Suche ist ausschließlich die rollenbeschränkte Filterkonfiguration.
        if (request.FormData is null && request.FieldKey is null) return DirectorySubjectSelectionPolicy.WorkflowModeling;
        if (request.FormData is null || string.IsNullOrWhiteSpace(request.FieldKey)) return null;
        var preview = await authoring.PreviewAsync(formId, request.FormData);
        return FormContractCompiler.Compile(preview.FormData).Fields
            .SingleOrDefault(field => field.Key == request.FieldKey)?.SubjectSelection;
    }

    private async Task<DirectorySnapshot?> Snapshot()
    {
        try { return await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
        catch (NotSupportedException) { return null; }
    }
    private ObjectResult Unavailable() => Problem(statusCode: 503, title: "Directory unavailable",
        detail: "No successfully synchronized identity-directory snapshot is available.");
    private static DirectorySubjectDto ToDto(DirectorySubjectResult item) => new()
    {
        Subject = new() { Kind = item.Subject.Kind == Model.DirectorySubjectKind.User ? "user" : "group", Id = item.Subject.Id },
        DisplayName = item.DisplayName, Detail = item.Detail, IsActive = item.IsActive, IsSelectable = item.IsSelectable,
        Email = item.Email, Username = item.Username, FirstName = item.FirstName, LastName = item.LastName
    };
}
