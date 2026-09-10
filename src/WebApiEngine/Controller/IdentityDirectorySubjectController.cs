using Microsoft.AspNetCore.Mvc;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Gibt keine allgemeine Personenliste frei, sondern sucht nur im Kontext eines Workflows
/// oder Ordners, den die aufrufende Person tatsächlich bearbeiten beziehungsweise delegieren darf.
/// </summary>
[ApiController, Route("identity-directory")]
public sealed class IdentityDirectorySubjectController(
    IStorageSystem storageSystem,
    FolderBusinessLogic folderBusinessLogic,
    DirectorySubjectSelectionService selectionService) : ControllerBase
{
    private const string HiddenResourceTitle = "Identity directory search unavailable";
    private const string HiddenResourceDetail = "The resource or directory search is not available.";

    [HttpGet("workflows/{definitionId}/subjects")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> Search(
        [FromRoute] string definitionId,
        [FromQuery] string? query,
        [FromQuery] string kind = "all",
        [FromQuery] int limit = 20)
    {
        var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        var metaDefinition = metaDefinitions.FirstOrDefault(entry => entry.DefinitionId == definitionId);
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (metaDefinition is null || !permissions.MayEditIn(metaDefinition.FolderId))
        {
            return HiddenNotFound();
        }

        return await SearchDirectory(query, kind, limit, permissions.DirectorySnapshot);
    }

    /// <summary>
    /// Sucht nur fuer die Pflege genau dieses Ordners. Ein fehlendes Delegationsrecht wird wie
    /// ein unbekannter Ordner behandelt, damit die Suche keine fremden Strukturen offenlegt.
    /// </summary>
    [HttpGet("folders/{folderId:guid}/subjects")]
    [ProducesResponseType<ApiStatusResult<DirectorySubjectSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchForFolder(
        [FromRoute] Guid folderId,
        [FromQuery] string? query,
        [FromQuery] string kind = "all",
        [FromQuery] int limit = 20)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!permissions.Folders.Any(folder => folder.Id == folderId)
            || !permissions.MayDelegateIn(folderId))
        {
            return HiddenNotFound();
        }

        return await SearchDirectory(query, kind, limit, permissions.DirectorySnapshot);
    }

    private async Task<ActionResult<ApiStatusResult<DirectorySubjectSearchResultDto>>> SearchDirectory(
        string? query,
        string kind,
        int limit,
        DirectorySnapshot? authorizedSnapshot)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length is < 2 or > 100)
        {
            return InvalidSearch("The query must contain between 2 and 100 non-whitespace characters.");
        }

        if (!TryParseKind(kind, out var requestedKind))
        {
            return InvalidSearch("The kind must be one of: all, user, group.");
        }

        if (limit is < 1 or > 50)
        {
            return InvalidSearch("The limit must be between 1 and 50.");
        }

        var result = authorizedSnapshot is null
            ? await selectionService.SearchAsync(
                query,
                requestedKind,
                limit,
                DirectorySubjectSelectionPolicy.WorkflowModeling)
            : DirectorySubjectSelectionService.Search(
                authorizedSnapshot,
                query,
                requestedKind,
                limit,
                DirectorySubjectSelectionPolicy.WorkflowModeling);
        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: HiddenResourceTitle,
                detail: "No successfully synchronized identity-directory snapshot is available.");
        }

        return Ok(new ApiStatusResult<DirectorySubjectSearchResultDto>(new DirectorySubjectSearchResultDto
        {
            GenerationId = result.GenerationId,
            Items = result.Items.Select(ToDto).ToList()
        }));
    }

    private ObjectResult HiddenNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: HiddenResourceTitle,
        detail: HiddenResourceDetail);

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
