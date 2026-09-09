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
        // Der Parser liest aus Kompatibilitaetsgruenden auch historische, nicht ausführbare
        // Typen. Neue Uploads duerfen sie jedoch nicht als startbare Workflow-Version ablegen.
        BpmnCapabilityMatrix.ValidateForDeployment(rawContent);
        var model = ModelParser.ParseModel(rawContent);

        // Die Kennung stammt aus dem hochgeladenen XML (definitions/@id) und wird in der
        // Dateiablage zum Dateinamen. Vor jedem Schreiben pruefen, sonst legt schon der Upload
        // eine Version unter einem Pfad ausserhalb der Ablage an. Die ArgumentException wird
        // von der Fehlerbehandlung der API zu einer 400 im gewohnten Umschlag.
        DefinitionIdRules.EnsureValid(model.Id, "definitions/@id");

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
            return ModelParser.ParseModel(rawContent).Id;
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
