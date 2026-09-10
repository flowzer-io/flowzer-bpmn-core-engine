using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Model;
using StorageSystem;
using WebApiEngine.Auth;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Laedt den Ordnerbaum und die daraus folgenden Rechte der aufrufenden Person.
///
/// Bewusst ein eigener Schritt vor jeder schreibenden Handlung: Die Ordnerrechte haengen an
/// Daten, nicht an einem Claim, und lassen sich deshalb nicht wie die Anwendungsrollen mit einem
/// Attribut am Endpunkt pruefen.
/// </summary>
public class FolderBusinessLogic(
    IStorageSystem storageSystem,
    ICurrentUserContextAccessor currentUserContextAccessor,
    IAuthorizationService authorizationService)
{
    public async Task<FolderPermissions> LoadPermissionsAsync(ClaimsPrincipal user)
    {
        var folders = await storageSystem.FolderStorage.GetAllFolders();
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var isGlobalModeler = (await authorizationService.AuthorizeAsync(user, FlowzerPolicies.Modeler)).Succeeded;
        DirectorySnapshot? directorySnapshot = null;
        if (!isGlobalModeler && folders.SelectMany(folder => folder.Assignments)
            .Any(assignment => assignment.AssignmentMode == FolderAssignmentMode.Directory))
        {
            try { directorySnapshot = await storageSystem.IdentityDirectoryStorage.GetActiveSnapshot(); }
            catch (NotSupportedException) { /* Directory-Rechte fallen ohne Snapshot geschlossen aus. */ }
        }

        return new FolderPermissions(
            folders,
            FolderAccess.ResolveRoles(folders, currentUser, directorySnapshot),
            isGlobalModeler,
            directorySnapshot);
    }

    /// <summary>
    /// Der Ordner, in dem ein Workflow liegt. <c>null</c> heisst oberste Ebene — und ebenso, wenn
    /// es den Katalogeintrag gar nicht gibt: Dann entscheidet der Aufrufer ueber die oberste
    /// Ebene, und das ist die strengere Annahme.
    /// </summary>
    public async Task<Guid?> GetFolderOfDefinitionAsync(string definitionId)
    {
        var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        return metaDefinitions.FirstOrDefault(metaDefinition => metaDefinition.DefinitionId == definitionId)?.FolderId;
    }

    /// <summary>Anzahl der Workflows je Ordner, ohne Unterordner.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> CountWorkflowsPerFolderAsync()
    {
        var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        return metaDefinitions
            .Where(metaDefinition => metaDefinition.FolderId.HasValue)
            .GroupBy(metaDefinition => metaDefinition.FolderId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Prueft, ob ein Ordner als Ziel taugt: Er muss existieren. <c>null</c> ist die oberste
    /// Ebene und immer gueltig.
    /// </summary>
    public static bool IsKnownTarget(Guid? folderId, IReadOnlyCollection<WorkflowFolder> folders) =>
        folderId is not { } id || folders.Any(folder => folder.Id == id);
}
