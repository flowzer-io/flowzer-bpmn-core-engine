using BPMN.Infrastructure;
using core_engine;
using Model;
using System.Security.Cryptography;
using System.Text;
using WebApiEngine.Auth;
using WebApiEngine.Ai;
using Version = Model.Version;

namespace WebApiEngine.BusinessLogic;

public class DefinitionBusinessLogic(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserContextAccessor,
    IAiSecretStore aiSecretStore,
    AiToolRegistry aiToolRegistry)
{
    
    public async Task<BpmnDefinition> StoreDefinition(string rawContent, Guid? previousGuid, bool deploy = false)
    {
        // Definition und XML gehoeren zusammen: eine Transaktion, ein Commit.
        using var storageSystem = storageProvider.GetTransactionalStorage();
        // Nur der XML-Umschlag und die sichere Katalogkennung sind beim Speichern
        // verpflichtend. Unvollständige Fachkonfiguration ist ein zulässiger Entwurf.
        var definitionId = DefinitionDraftValidator.ReadDefinitionId(rawContent);

        // Auth zuerst: Ohne aufgelösten Benutzer bleibt die Antwort 401,
        // unabhängig davon, ob die Meta-Definition existiert.
        var currentUser = currentUserContextAccessor.GetCurrentUser();
        var resolvedUserId = currentUser.RequireResolvedUserId("definition changes");

        // Die Versionsnummer entsteht aus der bisher hoechsten. Ein zweiter API-Prozess, der
        // denselben Workflow gleichzeitig speichert, muss warten — sonst vergeben beide
        // dieselbe Nummer und einer scheitert am Unique-Index statt mit der naechsten Version.
        await storageSystem.DefinitionStorage.LockForDefinitionChange(definitionId);

        if (deploy)
        {
            BpmnCapabilityMatrix.ValidateForDeployment(rawContent);
            var model = ModelParser.ParseModel(rawContent);
            await AiTaskDeploymentValidator.ValidateAsync(model, storageSystem.AiConnectionStorage, aiSecretStore, aiToolRegistry);
            // Ein Deploy ohne Meta-Definition hinterlässt Instanzen, deren Katalog-
            // Eintrag fehlt, und macht damit die komplette Instanzliste unbrauchbar.
            var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
            if (metaDefinitions.All(metaDefinition => metaDefinition.DefinitionId != definitionId))
            {
                throw new InvalidOperationException(
                    $"No meta definition found for definitionId {definitionId}. " +
                    "Create the workflow in the catalog first, then deploy it.");
            }
        }

        var highestVersion = await storageSystem.DefinitionStorage.GetMaxVersionId(definitionId);
        
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
            DefinitionId = definitionId,
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

    /// <summary>
    /// Liest die Kennung der Definition aus dem BPMN-XML, ohne bei ungueltigem Inhalt zu werfen.
    ///
    /// Gebraucht wird sie fuer die Rechtepruefung: Erst die Kennung sagt, in welchem Ordner der
    /// Workflow liegt. Ist das XML unbrauchbar, ist das kein Rechteproblem — dann meldet der
    /// eigentliche Speicherpfad den Fehler, und zwar mit seiner Begruendung.
    /// </summary>
    public static string? TryReadDefinitionId(string rawContent)
    {
        try
        {
            return DefinitionDraftValidator.ReadDefinitionId(rawContent);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeStableHash(string rawContent)
    {
        var contentBytes = Encoding.UTF8.GetBytes(rawContent);
        var hashBytes = SHA256.HashData(contentBytes);
        return Convert.ToHexString(hashBytes);
    }

}
