using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

public class InstanceStorage : IInstanceStorage
{
    private readonly string _instancesPath;
    private readonly JsonSerializerSettings _newtonSoftDefaultSettings;

    public InstanceStorage(Storage storage)
    {
        _instancesPath = storage.GetBasePath("FileStorage/Instances");
        
        _newtonSoftDefaultSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            Formatting = Formatting.Indented,
        };
    }

    public async Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
    {
        var path = Directory.GetFiles(_instancesPath, $"instance_*_{processInstanceId}.json").Single();
        var content = await File.ReadAllTextAsync(path);
        return JsonConvert.DeserializeObject<ProcessInstanceInfo>(content, _newtonSoftDefaultSettings)!;
    }

    public async Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
    {
        var fullFileName = Path.Combine(_instancesPath, $"instance_{processInstanceInfo.metaDefinitionId}_{processInstanceInfo.InstanceId}.json");
        var data = JsonConvert.SerializeObject(processInstanceInfo, _newtonSoftDefaultSettings);
        await StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public async Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances()
    {
        return (await GetAllInstances()).Where(x => !x.IsFinished);
    }

    public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances()
    {
        var instances = StorageFile.ReadExistingFiles(_instancesPath, "*.json")
            .Select(entry => JsonConvert.DeserializeObject<ProcessInstanceInfo>(entry.Content, _newtonSoftDefaultSettings)!)
            .ToList();

        return Task.FromResult<IEnumerable<ProcessInstanceInfo>>(instances);
    }
}
