using BPMN.Infrastructure;
using core_engine;
using Model;
using WebApiEngine.Auth;
using Version = Model.Version;

namespace WebApiEngine.BusinessLogic;

public class DefinitionBusinessLogic(
    IStorageSystem storageSystem,
    ICurrentUserContextAccessor currentUserContextAccessor)
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
    
        
                
        var currentUser = currentUserContextAccessor.GetCurrentUser();

        var definition = new BpmnDefinition()
        {
            Id = Guid.NewGuid(),
            DefinitionId = model.Id,
            PreviousGuid = previousGuid,
            Hash = rawContent.GetHashCode().ToString(),
            SavedByUser = currentUser.UserId,
            SavedOn = DateTime.UtcNow,
            Version = highestVersion,
            IsActive = false
        };

        await storageSystem.DefinitionStorage.StoreDefinition(definition);
        await storageSystem.DefinitionStorage.StoreBinary(definition.Id, rawContent);

        return definition;
    }
}
