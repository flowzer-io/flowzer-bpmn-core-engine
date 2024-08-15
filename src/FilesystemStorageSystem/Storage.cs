using StorageSystem;

namespace FilesystemStorageSystem;


public class Storage : IStorageSystem
{
    
    public Storage()
    {
        SubscriptionStorage = new MessageSubscriptionStorage(this);
        DefinitionStorage = new DefinitionStorage(this);
        InstanceStorage = new InstanceStorage(this);
    }

    public IMessageSubscriptionStorage SubscriptionStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IDefinitionStorage DefinitionStorage { get; set; }

    public string GetBasePath(string name)
    {
        var path = Path.Combine(Path.GetDirectoryName(this.GetType().Assembly.Location)!, name);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }
    
    

}

public class TransactionalStorage : Storage, ITransactionalStorage
{
    public void CommitChanges()
    {
        return;
    }

    public void RollbackTransaction()
    {
        return;
    }

    public void Dispose()
    {
        return;
    }
}