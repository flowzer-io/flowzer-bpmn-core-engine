using BPMN.Flowzer.Events;
using Version = Model.Version;

namespace StorageSystem;

public interface IDefinitionStorage
{
    Task StoreBinary(Guid guid, string data);

    Task<string> GetBinary(Guid guid);
    Task<Guid[]> GetAllBinaryDefinitions();
    Task<BpmnDefinition[]> GetAllDefinitions();
    Task StoreDefinition(BpmnDefinition definition);
    Task<Version?> GetMaxVersionId(string modelId);

    Task<BpmnDefinition> GetDefinitionById(Guid id);
    Task<BpmnDefinition> GetLatestDefinition(string definitionId);
    Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId);

    #region "Meta"

    Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions();
    Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition);
    Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition);
    Task<BpmnMetaDefinition> GetMetaDefinitionById(string id);

    #endregion
}