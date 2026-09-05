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
        // Eine fehlende Instanz ist ein erwartbarer 404, kein Serverfehler. Die Datei kann
        // zwischen Listing und Lesen auch von einem parallelen Vorgang ersetzt werden.
        var path = Directory.GetFiles(_instancesPath, $"instance_*_{processInstanceId}.json").SingleOrDefault();
        var content = path is null ? null : await StorageFile.ReadAllTextIfExistsAsync(path);
        if (content is null)
        {
            throw new FileNotFoundException($"Process instance {processInstanceId} was not found.");
        }

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

    public Task DeleteInstance(Guid processInstanceId)
    {
        // Der Dateiname traegt die Definition mit, die zur Zeit des Schreibens galt; gesucht
        // wird deshalb ueber das Muster und nicht ueber einen zusammengesetzten Pfad.
        foreach (var path in Directory.GetFiles(_instancesPath, $"instance_*_{processInstanceId}.json"))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances()
    {
        var instances = StorageFile.ReadExistingFiles(_instancesPath, "*.json")
            .Select(entry => JsonConvert.DeserializeObject<ProcessInstanceInfo>(entry.Content, _newtonSoftDefaultSettings)!)
            .ToList();

        return Task.FromResult<IEnumerable<ProcessInstanceInfo>>(instances);
    }
}
