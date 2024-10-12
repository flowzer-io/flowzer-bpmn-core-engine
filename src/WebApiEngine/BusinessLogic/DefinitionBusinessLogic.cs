using BPMN.Infrastructure;
using core_engine;
using Model;
using Version = Model.Version;

namespace WebApiEngine.BusinessLogic;

public class DefinitionBusinessLogic(IStorageSystem storageSystem)
{
    
    public async Task<BpmnDefinition> StoreDefinition(string rawContent, Guid? previousGuid, bool deploy = false)
    {
        
        var model = ModelParser.ParseModel(rawContent);
        

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
            Hash = rawContent.GetHashCode().ToString(),
            SavedByUser = Guid.Parse("D266F2B6-E96E-4D4A-9C20-C8E541394DF0"), // User.Claims["guid"] or something like that
            SavedOn = DateTime.UtcNow,
            Version = highestVersion,
            IsActive = false
        };

        await storageSystem.DefinitionStorage.StoreDefinition(definition);
        await storageSystem.DefinitionStorage.StoreBinary(definition.Id, rawContent);

        return definition;
    }
}