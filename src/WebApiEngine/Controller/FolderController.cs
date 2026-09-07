using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Ordner des Workflow-Katalogs und die Zustaendigkeiten, die an ihnen haengen.
///
/// Lesen steht jeder zugelassenen Person offen — wie beim Katalog selbst. Aendern verlangt die
/// Fachverantwortung fuer den betroffenen Ordner oder die Anwendungsrolle fuers Modellieren.
/// Weil diese Bedingung an Daten haengt und nicht an einem Claim, steht sie im Rumpf der
/// Methoden und nicht in einem <c>Authorize</c>-Attribut.
/// </summary>
[ApiController, Route("[controller]")]
public class FolderController(
    ITransactionalStorageProvider storageProvider,
    FolderBusinessLogic folderBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor) : FlowzerControllerBase
{
    /// <summary>Grenze fuer den Namen eines Ordners.</summary>
    private const int MaxFolderNameLength = 120;

    /// <summary>
    /// Der vollstaendige Ordnerbaum als flache Liste. Flach und nicht verschachtelt, weil die
    /// Oberflaeche den Baum ohnehin selbst aufbaut und eine flache Liste sich ohne Sonderfall
    /// filtern und suchen laesst.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<WorkflowFolderDto[]>>> GetFolders()
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        var counts = await folderBusinessLogic.CountWorkflowsPerFolderAsync();

        var dtos = permissions.Folders
            .Select(folder =>
            {
                var dto = folder.ToDto();
                dto.MayEdit = permissions.MayEditIn(folder.Id);
                dto.MayDelegate = permissions.MayDelegateIn(folder.Id);
                dto.WorkflowCount = counts.TryGetValue(folder.Id, out var count) ? count : 0;
                dto.InheritedAssignments = FolderAccess.CollectInherited(folder, permissions.Folders)
                    .Select(entry => entry.Assignment.ToInheritedDto(entry.Source))
                    .ToList();
                return dto;
            })
            .ToArray();

        return Ok(new ApiStatusResult<WorkflowFolderDto[]>(dtos));
    }

    [HttpPost]
    public async Task<ActionResult<ApiStatusResult<WorkflowFolderDto>>> CreateFolder([FromBody] WorkflowFolderRequestDto request)
    {
        if (NormalizeName(request.Name) is not { } name)
        {
            return BadRequest(new ApiStatusResult<WorkflowFolderDto>(
                $"Der Name eines Ordners darf nicht leer sein und hoechstens {MaxFolderNameLength} Zeichen lang."));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!FolderBusinessLogic.IsKnownTarget(request.ParentId, permissions.Folders))
        {
            return NotFound(new ApiStatusResult<WorkflowFolderDto>($"Es gibt keinen Ordner mit der Kennung {request.ParentId}."));
        }

        if (!permissions.MayDelegateIn(request.ParentId))
        {
            return ForbiddenCapability<WorkflowFolderDto>(request.ParentId is null
                ? "Ordner auf oberster Ebene anzulegen ist der Rolle fuers Modellieren vorbehalten."
                : "Fuer diesen Ordner fehlt Ihnen die Fachverantwortung.");
        }

        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var folder = new WorkflowFolder
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = request.ParentId,
            Description = NormalizeDescription(request.Description),
            CreatedOn = DateTime.UtcNow,
            CreatedByUser = currentUser.UserId
        };

        using var storage = storageProvider.GetTransactionalStorage();
        await storage.FolderStorage.StoreFolder(folder);
        storage.CommitChanges();

        return Ok(new ApiStatusResult<WorkflowFolderDto>(folder.ToDto()));
    }

    /// <summary>Benennt einen Ordner um und verschiebt ihn, wenn <c>ParentId</c> abweicht.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiStatusResult<WorkflowFolderDto>>> UpdateFolder(
        [FromRoute] Guid id,
        [FromBody] WorkflowFolderRequestDto request)
    {
        if (NormalizeName(request.Name) is not { } name)
        {
            return BadRequest(new ApiStatusResult<WorkflowFolderDto>(
                $"Der Name eines Ordners darf nicht leer sein und hoechstens {MaxFolderNameLength} Zeichen lang."));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (permissions.Folders.FirstOrDefault(candidate => candidate.Id == id) is not { } folder)
        {
            return NotFound(new ApiStatusResult<WorkflowFolderDto>($"Es gibt keinen Ordner mit der Kennung {id}."));
        }

        if (!permissions.MayDelegateIn(id))
        {
            return ForbiddenCapability<WorkflowFolderDto>("Fuer diesen Ordner fehlt Ihnen die Fachverantwortung.");
        }

        if (request.ParentId != folder.ParentId)
        {
            if (!FolderBusinessLogic.IsKnownTarget(request.ParentId, permissions.Folders))
            {
                return NotFound(new ApiStatusResult<WorkflowFolderDto>($"Es gibt keinen Ordner mit der Kennung {request.ParentId}."));
            }

            // Verschieben heisst, den Ast woanders einzuhaengen. Wer das darf, muss auch am Ziel
            // zustaendig sein — sonst liesse sich ein Ordner in fremde Verantwortung schieben.
            if (!permissions.MayDelegateIn(request.ParentId))
            {
                return ForbiddenCapability<WorkflowFolderDto>(request.ParentId is null
                    ? "Ordner auf die oberste Ebene zu verschieben ist der Rolle fuers Modellieren vorbehalten."
                    : "Fuer den Zielordner fehlt Ihnen die Fachverantwortung.");
            }

            if (FolderAccess.WouldCreateCycle(id, request.ParentId, permissions.Folders))
            {
                return BadRequest(new ApiStatusResult<WorkflowFolderDto>(
                    "Ein Ordner kann nicht in sich selbst oder in einen seiner Unterordner verschoben werden."));
            }
        }

        folder.Name = name;
        folder.ParentId = request.ParentId;
        folder.Description = NormalizeDescription(request.Description);

        using var storage = storageProvider.GetTransactionalStorage();
        await storage.FolderStorage.UpdateFolder(folder);
        storage.CommitChanges();

        return Ok(new ApiStatusResult<WorkflowFolderDto>(folder.ToDto()));
    }

    /// <summary>
    /// Loescht einen leeren Ordner.
    ///
    /// Nur leer: Ein Ordner mit Unterordnern oder Workflows mitzuloeschen waere ein einzelner
    /// Klick, hinter dem beliebig viel Arbeit anderer Menschen haengt. Was drin ist, muss vorher
    /// verschoben oder einzeln geloescht werden.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiStatusResult<WorkflowFolderDto>>> DeleteFolder([FromRoute] Guid id)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (permissions.Folders.FirstOrDefault(candidate => candidate.Id == id) is not { } folder)
        {
            return NotFound(new ApiStatusResult<WorkflowFolderDto>($"Es gibt keinen Ordner mit der Kennung {id}."));
        }

        if (!permissions.MayDelegateIn(id))
        {
            return ForbiddenCapability<WorkflowFolderDto>("Fuer diesen Ordner fehlt Ihnen die Fachverantwortung.");
        }

        var subFolders = permissions.Folders.Count(candidate => candidate.ParentId == id);
        var counts = await folderBusinessLogic.CountWorkflowsPerFolderAsync();
        var workflows = counts.TryGetValue(id, out var count) ? count : 0;

        if (subFolders > 0 || workflows > 0)
        {
            return Conflict(new ApiStatusResult<WorkflowFolderDto>(
                $"Der Ordner enthaelt noch {subFolders} Unterordner und {workflows} Workflow(s). Erst leeren, dann loeschen."));
        }

        using var storage = storageProvider.GetTransactionalStorage();
        await storage.FolderStorage.DeleteFolder(id);
        storage.CommitChanges();

        return Ok(new ApiStatusResult<WorkflowFolderDto>(folder.ToDto()));
    }

    /// <summary>
    /// Setzt die Zuweisungen eines Ordners neu — vollstaendig, nicht ergaenzend: Die Oberflaeche
    /// zeigt die Liste als Ganzes, und ein Entfernen muss auch als Entfernen ankommen.
    ///
    /// Geerbte Zuweisungen sind hier nicht dabei und koennen es auch nicht werden; sie stehen im
    /// uebergeordneten Ordner und werden dort gepflegt.
    /// </summary>
    [HttpPut("{id:guid}/assignments")]
    public async Task<ActionResult<ApiStatusResult<WorkflowFolderDto>>> UpdateAssignments(
        [FromRoute] Guid id,
        [FromBody] FolderAssignmentsRequestDto request)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (permissions.Folders.FirstOrDefault(candidate => candidate.Id == id) is not { } folder)
        {
            return NotFound(new ApiStatusResult<WorkflowFolderDto>($"Es gibt keinen Ordner mit der Kennung {id}."));
        }

        if (!permissions.MayDelegateIn(id))
        {
            return ForbiddenCapability<WorkflowFolderDto>("Fuer diesen Ordner fehlt Ihnen die Fachverantwortung.");
        }

        List<FolderAssignment> assignments;
        try
        {
            assignments = request.Assignments.Select(dto => dto.ToModel()).ToList();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ApiStatusResult<WorkflowFolderDto>(exception.Message));
        }

        // Dieselbe Kennung zweimal mit verschiedenen Rollen waere nicht entscheidbar; die
        // staerkere gewinnt, und die Antwort zeigt, was tatsaechlich gespeichert wurde.
        folder.Assignments = assignments
            .GroupBy(assignment => (assignment.SubjectKind, assignment.Subject.ToLowerInvariant()))
            .Select(group => group.OrderByDescending(assignment => assignment.Role).First())
            .ToList();

        using var storage = storageProvider.GetTransactionalStorage();
        await storage.FolderStorage.UpdateFolder(folder);
        storage.CommitChanges();

        var dto = folder.ToDto();
        dto.MayEdit = true;
        dto.MayDelegate = true;
        dto.InheritedAssignments = FolderAccess.CollectInherited(folder, permissions.Folders)
            .Select(entry => entry.Assignment.ToInheritedDto(entry.Source))
            .ToList();

        return Ok(new ApiStatusResult<WorkflowFolderDto>(dto));
    }

    private static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxFolderNameLength ? null : trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
