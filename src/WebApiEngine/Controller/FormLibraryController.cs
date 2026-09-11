using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Ordner und datensparsame Versionsauswahl der gemeinsamen Formularbibliothek.</summary>
[ApiController]
[Route("form")]
public sealed class FormLibraryController(
    IStorageSystem storageSystem,
    ITransactionalStorageProvider storageProvider) : ControllerBase
{
    [HttpGet("{formId:guid}/versions")]
    [ProducesResponseType<ApiStatusResult<FormVersionSummaryDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<FormVersionSummaryDto[]>>> GetVersions(Guid formId)
    {
        try { _ = await storageSystem.FormStorage.GetFormMetaData(formId); }
        catch (FileNotFoundException)
        {
            return LibraryProblem(StatusCodes.Status404NotFound, "form.not_found", "The form was not found.");
        }

        var versions = (await storageSystem.FormStorage.GetForms(formId))
            .OrderByDescending(form => form.Version)
            .Select(form => new FormVersionSummaryDto
            {
                Id = form.Id,
                FormId = form.FormId,
                Version = form.Version.ToDto()
            })
            .ToArray();
        return Ok(new ApiStatusResult<FormVersionSummaryDto[]>(versions));
    }

    [HttpGet("folders")]
    [ProducesResponseType<ApiStatusResult<FormFolderDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<FormFolderDto[]>>> GetFolders()
    {
        var folders = (await storageSystem.FormStorage.GetFolders())
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(folder => folder.Id)
            .Select(ToDto)
            .ToArray();
        return Ok(new ApiStatusResult<FormFolderDto[]>(folders));
    }

    [HttpPost("folders")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<FormFolderDto>>> CreateFolder(FormFolderRequestDto request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 120)
            return LibraryProblem(StatusCodes.Status400BadRequest, "form_folder.name_invalid",
                "A folder name between 1 and 120 characters is required.");
        using var storage = storageProvider.GetTransactionalStorage();
        var folders = await storage.FormStorage.GetFolders();
        if (request.ParentId is { } parentId && folders.All(folder => folder.Id != parentId))
            return LibraryProblem(StatusCodes.Status404NotFound, "form_folder.parent_not_found",
                "The parent folder was not found.");
        if (HasSiblingWithName(folders, request.ParentId, name))
            return LibraryProblem(StatusCodes.Status409Conflict, "form_folder.name_conflict",
                "A folder with this name already exists here.");

        var folder = new FormFolder { Id = Guid.NewGuid(), ParentId = request.ParentId, Name = name };
        await storage.FormStorage.SaveFolder(folder);
        storage.CommitChanges();
        return Created($"/form/folders/{folder.Id}", new ApiStatusResult<FormFolderDto>(ToDto(folder)));
    }

    [HttpPut("folders/{folderId:guid}")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<FormFolderDto>>> UpdateFolder(
        Guid folderId,
        FormFolderRequestDto request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 120)
            return LibraryProblem(StatusCodes.Status400BadRequest, "form_folder.name_invalid",
                "A folder name between 1 and 120 characters is required.");
        using var storage = storageProvider.GetTransactionalStorage();
        var folders = await storage.FormStorage.GetFolders();
        var folder = folders.SingleOrDefault(item => item.Id == folderId);
        if (folder is null)
            return LibraryProblem(StatusCodes.Status404NotFound, "form_folder.not_found", "The form folder was not found.");
        if (request.ParentId is { } parentId && folders.All(item => item.Id != parentId))
            return LibraryProblem(StatusCodes.Status404NotFound, "form_folder.parent_not_found",
                "The parent folder was not found.");
        if (WouldCreateFolderCycle(folderId, request.ParentId, folders))
            return LibraryProblem(StatusCodes.Status409Conflict, "form_folder.cycle",
                "A folder cannot be moved below itself.");
        if (HasSiblingWithName(folders, request.ParentId, name, folderId))
            return LibraryProblem(StatusCodes.Status409Conflict, "form_folder.name_conflict",
                "A folder with this name already exists here.");

        folder.Name = name;
        folder.ParentId = request.ParentId;
        await storage.FormStorage.UpdateFolder(folder);
        storage.CommitChanges();
        return Ok(new ApiStatusResult<FormFolderDto>(ToDto(folder)));
    }

    [HttpDelete("folders/{folderId:guid}")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<FormFolderDto>>> DeleteFolder(Guid folderId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var folders = await storage.FormStorage.GetFolders();
        var folder = folders.SingleOrDefault(item => item.Id == folderId);
        if (folder is null)
            return LibraryProblem(StatusCodes.Status404NotFound, "form_folder.not_found", "The form folder was not found.");
        if (folders.Any(item => item.ParentId == folderId)
            || (await storage.FormStorage.GetFormMetadatas()).Any(form => form.FolderId == folderId))
            return LibraryProblem(StatusCodes.Status409Conflict, "form_folder.not_empty",
                "Only empty folders can be deleted.");

        await storage.FormStorage.DeleteFolder(folderId);
        storage.CommitChanges();
        return Ok(new ApiStatusResult<FormFolderDto>(ToDto(folder)));
    }

    [HttpPut("meta/{formId:guid}/folder")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<FormMetaDataDto>>> MoveForm(
        Guid formId,
        MoveFormRequestDto request)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        FormMetadata metadata;
        try { metadata = await storage.FormStorage.GetFormMetaData(formId); }
        catch (FileNotFoundException)
        {
            return LibraryProblem(StatusCodes.Status404NotFound, "form.not_found", "The form was not found.");
        }
        if (request.FolderId is { } folderId && await storage.FormStorage.GetFolder(folderId) is null)
            return LibraryProblem(StatusCodes.Status404NotFound, "form_folder.not_found", "The form folder was not found.");

        metadata.FolderId = request.FolderId;
        await storage.FormStorage.UpdateFormMetaData(metadata);
        storage.CommitChanges();
        return Ok(new ApiStatusResult<FormMetaDataDto>(metadata.ToDto()));
    }

    private static FormFolderDto ToDto(FormFolder folder) => new()
    {
        Id = folder.Id,
        ParentId = folder.ParentId,
        Name = folder.Name
    };

    private static bool HasSiblingWithName(
        IEnumerable<FormFolder> folders,
        Guid? parentId,
        string name,
        Guid? exceptId = null) => folders.Any(folder =>
            folder.Id != exceptId
            && folder.ParentId == parentId
            && string.Equals(folder.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private static bool WouldCreateFolderCycle(
        Guid folderId,
        Guid? parentId,
        IReadOnlyCollection<FormFolder> folders)
    {
        var byId = folders.ToDictionary(folder => folder.Id);
        var visited = new HashSet<Guid>();
        while (parentId is { } current)
        {
            if (current == folderId || !visited.Add(current)) return true;
            parentId = byId.GetValueOrDefault(current)?.ParentId;
        }
        return false;
    }

    private ObjectResult LibraryProblem(int status, string code, string detail)
    {
        var problem = new WebApiEngine.Middleware.ApiProblemDetails
        {
            Status = status,
            Title = "The form library request could not be completed.",
            Detail = detail,
            Type = "about:blank",
            Instance = Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = new ObjectResult(problem) { StatusCode = status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
