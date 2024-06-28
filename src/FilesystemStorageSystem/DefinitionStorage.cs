using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

public class DefinitionStorage(Storage storage) : IDefinitionStorage
{
    private readonly string _binaryBasePath = storage.GetBasePath("Definitions/Binary");
    private readonly string _basePath = storage.GetBasePath("Definitions");
    private readonly string _metabasePath = storage.GetBasePath("Definitions/Meta");
    


    public async Task StoreBinary(Guid guid, string data)
    {
        var fullFileName = Path.Combine(_binaryBasePath, $"{guid}.json");
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<string> GetBinary(Guid guid)
    {
        var fullFileName = Path.Combine(_binaryBasePath, $"{guid}.json");
        return File.ReadAllTextAsync(fullFileName);
    }

    public Task<Guid[]> GetAllBinaryDefinitions()
    {
        
        return Task.FromResult(Directory.GetFiles(_binaryBasePath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(Guid.Parse!).ToArray());
    }

    public Task<BpmnDefinition[]> GetAllDefinitions()
    {
        var definitions = new List<BpmnDefinition>();
        foreach (var file in Directory.GetFiles(_basePath, "*.json"))
        {
            var content = File.ReadAllText(file);
            definitions.Add(JsonConvert.DeserializeObject<BpmnDefinition>(content)!);
        }
        return Task.FromResult(definitions.ToArray());
    }

    public Task StoreDefinition(BpmnDefinition definition)
    {
        var fullFileName = Path.Combine(_basePath, $"{definition.Id}.json");
        var data = JsonConvert.SerializeObject(definition);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public async Task<BpmnVersion?> GetMaxVersionId(string modelId)
    {
        var definitions = await GetAllDefinitions();
        var maxVersionId = definitions.Where(x => x.DefinitionId == modelId)
            .Max(x => x.Version);
        return maxVersionId;

    }

    public Task<BpmnMetaDefinition[]> GetAllMetaDefinitions()
    {
        var definitions = new List<BpmnMetaDefinition>();
        foreach (var file in Directory.GetFiles(_metabasePath, "*.json"))
        {
            var content = File.ReadAllText(file);
            definitions.Add(JsonConvert.DeserializeObject<BpmnMetaDefinition>(content)!);
        }
        return Task.FromResult(definitions.ToArray());
    }

    public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
    {
        var fullFileName = Path.Combine(_metabasePath, $"{metaDefinition.DefinitionId}.json");
        var data = JsonConvert.SerializeObject(metaDefinition);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
    {
        var fullFileName = Path.Combine(_metabasePath, $"{id}.json");
        var content = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<BpmnMetaDefinition>(content)!);
    }

    public async Task<BpmnDefinition> GetDefinitionById(string id)
    {
        var fullFileName = Path.Combine(_basePath, $"{id}.json");
        var content = File.ReadAllText(fullFileName);
        return JsonConvert.DeserializeObject<BpmnDefinition>(content)!;
    }

    public async Task<BpmnDefinition> GetLatestDefinition(string definitionId)
    {
        var definitions = await GetAllDefinitions();
        var latestDefinition = definitions.Where(x => x.DefinitionId == definitionId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
        return latestDefinition;
    }
}