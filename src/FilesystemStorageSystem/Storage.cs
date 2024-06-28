using StorageSystem;

namespace FilesystemStorageSystem;

public class Storage : IStorageSystem
{
    
    
    public Storage()
    {
        InstanceStorage = new InstanceStorage(this);
        MessageSubscriptionStorage = new MessageSubscriptionyStorage(this);
        ProcessStorage = new ProcessStorage(this);
        DefinitionStorage = new DefinitionStorage(this);
    }

    public IProcessStorage ProcessStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IMessageSubscriptionStorage MessageSubscriptionStorage { get; }
    public IDefinitionStorage DefinitionStorage { get; set; }

    public string GetBasePath(string name)
    {
        var path = Path.Combine(System.IO.Path.GetDirectoryName(this.GetType().Assembly.Location)!, name);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }
}