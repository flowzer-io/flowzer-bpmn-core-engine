using BPMN.Infrastructure;
using core_engine;
using Model;
using System.Security.Cryptography;
using System.Text;
using WebApiEngine.Auth;
using Version = Model.Version;

namespace WebApiEngine.BusinessLogic;

public class DefinitionBusinessLogic(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserContextAccessor)
{
    
    public async Task<BpmnDefinition> StoreDefinition(string rawContent, Guid? previousGuid, bool deploy = false)
    {
        // Definition und XML gehoeren zusammen: eine Transaktion, ein Commit.
        using var storageSystem = storageProvider.GetTransactionalStorage();
        var model = ModelParser.ParseModel(rawContent);

        // Auth zuerst: Ohne aufgelösten Benutzer bleibt die Antwort 401,
        // unabhängig davon, ob die Meta-Definition existiert.
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var resolvedUserId = currentUser.RequireResolvedUserId("definition changes");

        if (deploy)
        {
            // Ein Deploy ohne Meta-Definition hinterlässt Instanzen, deren Katalog-
            // Eintrag fehlt, und macht damit die komplette Instanzliste unbrauchbar.
            var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
            if (metaDefinitions.All(metaDefinition => metaDefinition.DefinitionId != model.Id))
            {
                throw new InvalidOperationException(
                    $"No meta definition found for definitionId {model.Id}. " +
                    "Create the workflow in the catalog first, then deploy it.");
            }
        }

        var highestVersion = await storageSystem.DefinitionStorage.GetMaxVersionId(model.Id);
        
        if (highestVersion == null)
            highestVersion = new Version(1, 0);
        else
        {
            if (deploy)
            {
                highestVersion = new Version(highestVersion.Major +1, 0);
            }
            else
            {
                highestVersion = highestVersion + 1;
            }
            
        }
        var definition = new BpmnDefinition()
        {
            Id = Guid.NewGuid(),
            DefinitionId = model.Id,
            PreviousGuid = previousGuid,
            Hash = ComputeStableHash(rawContent),
            SavedByUser = resolvedUserId,
            SavedOn = DateTime.UtcNow,
            Version = highestVersion,
            IsActive = false
        };

        await storageSystem.DefinitionStorage.StoreDefinition(definition);
        await storageSystem.DefinitionStorage.StoreBinary(definition.Id, rawContent);
        storageSystem.CommitChanges();

        return definition;
    }

    private static string ComputeStableHash(string rawContent)
    {
        var contentBytes = Encoding.UTF8.GetBytes(rawContent);
        var hashBytes = SHA256.HashData(contentBytes);
        return Convert.ToHexString(hashBytes);
    }
}
