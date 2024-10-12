using Model;
using Newtonsoft.Json;
using StorageSystem;
using Version = Model.Version;

namespace FilesystemStorageSystem;

public class DefinitionStorage : IDefinitionStorage
{
    private readonly string _binaryBasePath;
    private readonly string _basePath;
    private readonly string _metabasePath;
    private readonly Storage _storage;

    public DefinitionStorage(Storage storage)
    {
        _storage = storage;
        _binaryBasePath = storage.GetBasePath("FileStorage/Definitions/Binary");
        _basePath = storage.GetBasePath("FileStorage/Definitions");
        _metabasePath = storage.GetBasePath("FileStorage/Definitions/Meta");

    }


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
        if (Directory.Exists(_binaryBasePath) == false)
            Directory.CreateDirectory(_binaryBasePath);
        
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
        var data = JsonConvert.SerializeObject(definition,  _storage.NewtonSoftDefaultSettings);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public async Task<Version?> GetMaxVersionId(string modelId)
    {
        var definitions = await GetAllDefinitions();
        var maxVersionId = definitions.Where(x => x.DefinitionId == modelId)
            .Max(x => x.Version);
        return maxVersionId;

    }

    public async Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
    {
        var ret = new List<ExtendedBpmnMetaDefinition>();
        
        if (Directory.Exists(_metabasePath) == false)
            Directory.CreateDirectory(_metabasePath);
        
        foreach (var file in Directory.GetFiles(_metabasePath, "*.json"))
        {
            var content = await File.ReadAllTextAsync(file);
            var bpmnMetaDefinition = JsonConvert.DeserializeObject<ExtendedBpmnMetaDefinition>(content)!;
            var bpmnDefinition = await GetLatestDefinition(bpmnMetaDefinition.DefinitionId);

            bpmnMetaDefinition.LatestVersion = bpmnDefinition.Version;
            bpmnMetaDefinition.LatestVersionDateTime = bpmnDefinition.SavedOn;

            var deployed = await GetDeployedDefinition(bpmnDefinition.DefinitionId);
            if (deployed != null)
            {
                bpmnMetaDefinition.DeployedId = deployed.Id;
                bpmnMetaDefinition.DeployedVersion = deployed.Version;
                bpmnMetaDefinition.DeployedVersionDateTime = deployed.SavedOn;
            }
            ret.Add(bpmnMetaDefinition);
        }
        
        return ret.ToArray();
    }

    public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
    {
        var fullFileName = Path.Combine(_metabasePath, $"{metaDefinition.DefinitionId}.json");
        if (File.Exists(fullFileName))
        {
            throw new Exception($"Meta definition already exists for definitionId {metaDefinition.DefinitionId}");
        }
        var data = JsonConvert.SerializeObject(metaDefinition, _storage.NewtonSoftDefaultSettings);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition)
    {
        var fullFileName = Path.Combine(_metabasePath, $"{metaDefinition.DefinitionId}.json");
        if (!File.Exists(fullFileName))
        {
            throw new Exception($"No meta definition found for definitionId {metaDefinition.DefinitionId}");
        }
        var data = JsonConvert.SerializeObject(metaDefinition,_storage.NewtonSoftDefaultSettings);
        return File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
    {
        var fullFileName = Path.Combine(_metabasePath, $"{id}.json");
        var content = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<BpmnMetaDefinition>(content)!);
    }

    public async Task<BpmnDefinition> GetDefinitionById(Guid id)
    {
        var fullFileName = Path.Combine(_basePath, $"{id}.json");
        var content = await File.ReadAllTextAsync(fullFileName);
        return JsonConvert.DeserializeObject<BpmnDefinition>(content)!;
    }

    public async Task<BpmnDefinition> GetLatestDefinition(string definitionId)
    {
        var definitions = await GetAllDefinitions();
        var latestDefinition = definitions.Where(x => x.DefinitionId == definitionId).MaxBy(x => x.Version);
        if (latestDefinition == null)
        {
            throw new Exception($"No definition found for definitionId {definitionId}");
        }
        return latestDefinition;
    }

    public async Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
    {
        return  (await GetAllDefinitions()).SingleOrDefault(x => x.DefinitionId == definitionDefinitionId && x.IsActive);
    }
}