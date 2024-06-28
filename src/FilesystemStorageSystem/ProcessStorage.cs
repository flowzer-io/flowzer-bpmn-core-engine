using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;


public class ProcessStorage : IProcessStorage
{
    private Storage _storage;
    private string _processesBasePath;

    public ProcessStorage(Storage storage)
    {
        _storage = storage;
        _processesBasePath = _storage.GetBasePath("Processes");
    }


    public IEnumerable<string> GetAllProcessDefinitionIds()
    {
        
        return Directory.GetFiles(_processesBasePath, "*.json").Select(Path.GetFileNameWithoutExtension).ToList()!;
    }

    public IEnumerable<ProcessInfo> GetAllProcessesInfos()
    {
        var processInfos = new List<ProcessInfo>();
        foreach (var file in Directory.GetFiles(_processesBasePath, "*.json"))
        {
            processInfos.Add(GetActiveProcessInfo(Path.GetFileNameWithoutExtension(file))!);
        }
        return processInfos;
    }

    public ProcessInfo GetActiveProcessInfo(string processId)
    {
        var fullFileName = Path.Combine(_processesBasePath, $"{processId}.json");
        return JsonConvert.DeserializeObject<ProcessInfo>(File.ReadAllText(fullFileName))!;
    }

    public void AddProcessInfo(ProcessInfo processInfo)
    {
        throw new NotImplementedException();
    }

    public void Add(ProcessInfo processInfo)
    {
        var fullFileName = Path.Combine(_processesBasePath, $"{processInfo.Process.Name}.json");
        File.WriteAllText(fullFileName, JsonConvert.SerializeObject(processInfo, Formatting.Indented));
    }

    public void DeactivateProcessInfo(string processId)
    {
        throw new NotImplementedException();
    }
}