using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;


public class Storage : IStorageSystem
{
    
    public Storage()
    {
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
        var path = Path.Combine(Path.GetDirectoryName(this.GetType().Assembly.Location)!, name);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }
    
    

}