using System.Runtime.InteropServices.ComTypes;
using BPMN.Common;
using core_engine;
using core_engine.Extensions;
using Model;

namespace WebApiEngine.BusinessLogic;

public class BpmnLogic(IDefinitionStorage definitionStorage, IInstanceStorage instanceStorage)
{
    
    public void Load()
    {
        
    }
    
    public async Task DeployDefinition(Guid versionedDefinitionId)
    {
        var definition = await definitionStorage.GetDefinitionById(versionedDefinitionId);
        var xmlData = await definitionStorage.GetBinary(versionedDefinitionId);
        var model =  ModelParser.ParseModel(xmlData);
        
        
        
        foreach (var process in model.GetProcesses())
        {
            var pe = new ProcessEngine(process);
            AddTimers(pe.ActiveTimers, versionedDefinitionId, definition.DefinitionId);
            StoreCatchmessages(pe.ActiveCatchMessages, versionedDefinitionId, definition.DefinitionId);
            StoreSignals(pe.ActiveCatchSignals, versionedDefinitionId, definition.DefinitionId);
        }
        
    }

    private void StoreSignals(List<SignalDefinition> signlas, Guid versionedDefinitionId, string metaDefinitionId)
    {
        
    }
    private void StoreCatchmessages(List<MessageDefinition> catachMessages, Guid versionedDefinitionId, string metaDefinitionId)
    {
    }

    private void AddTimers(List<DateTime> activeTimers, Guid versionedDefinitionId, string metaDefinitionId)
    {
        
    }
}