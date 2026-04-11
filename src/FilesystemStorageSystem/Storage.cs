using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;


public class Storage : IStorageSystem
{
    public const string StorageRootEnvironmentVariableName = "FLOWZER_STORAGE_ROOT";
    private readonly string _storageRoot;
    
    public Storage()
    {
        _storageRoot = ResolveStorageRoot();
        SubscriptionStorage = new MessageSubscriptionStorage(this);
        DefinitionStorage = new DefinitionStorage(this);
        InstanceStorage = new InstanceStorage(this);
        FormStorage = new FormStorage(this);
    }

    public IMessageSubscriptionStorage SubscriptionStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IFormStorage FormStorage { get; }
    public IDefinitionStorage DefinitionStorage { get; set; }

    public JsonSerializerSettings NewtonSoftDefaultSettings =>
        new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            Formatting = Formatting.Indented
        };

    public string GetBasePath(string name)
    {
        var path = Path.Combine(_storageRoot, name);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveStorageRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        return Path.GetDirectoryName(typeof(Storage).Assembly.Location)!
               ?? throw new InvalidOperationException("Could not determine the default filesystem storage root.");
    }

}
