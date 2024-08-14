using BPMN.Flowzer.Events;

namespace StorageSystem;

public interface IDefinitionStorage
{
    Task StoreBinary(Guid guid, string data);
    
    Task<string> GetBinary(Guid guid);
    Task<Guid[]> GetAllBinaryDefinitions();
    Task<BpmnDefinition[]> GetAllDefinitions();
    Task StoreDefinition(BpmnDefinition definition);
    Task <BpmnVersion?> GetMaxVersionId(string modelId);
    Task<BpmnMetaDefinition[]> GetAllMetaDefinitions();
    Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition);
    Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition);
    Task<BpmnMetaDefinition> GetMetaDefinitionById(string id);
    Task<BpmnDefinition> GetDefinitionById(Guid id);
    Task<BpmnDefinition> GetLatestDefinition(string definitionId);
}