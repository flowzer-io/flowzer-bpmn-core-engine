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
        UserTaskDraftStorage = new UserTaskDraftStorage(this);
        UserTaskLifecycleStorage = new UserTaskLifecycleStorage(this);
        UserTaskDeadlineStorage = new UserTaskDeadlineStorage(this);
        UserTaskNotificationStorage = new UserTaskNotificationStorage(this);
        SubscriptionStorage = new MessageSubscriptionStorage(this);
        DefinitionStorage = new DefinitionStorage(this);
        FolderStorage = new FolderStorage(this);
        InstanceStorage = new InstanceStorage(this);
        FormStorage = new FormStorage(this);
        ServiceTaskStorage = new ServiceTaskStorage(this);
        IdempotencyStorage = new IdempotencyStorage(this);
        IdentityDirectoryStorage = new IdentityDirectoryStorage(this);
    }

    public IMessageSubscriptionStorage SubscriptionStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IFormStorage FormStorage { get; }
    public IServiceTaskStorage ServiceTaskStorage { get; }
    public IIdempotencyStorage IdempotencyStorage { get; }
    public IIdentityDirectoryStorage IdentityDirectoryStorage { get; }
    public IUserTaskDraftStorage UserTaskDraftStorage { get; }
    public IUserTaskLifecycleStorage UserTaskLifecycleStorage { get; }
    public IUserTaskDeadlineStorage UserTaskDeadlineStorage { get; }
    public IUserTaskNotificationStorage UserTaskNotificationStorage { get; }
    public IDefinitionStorage DefinitionStorage { get; set; }
    public IFolderStorage FolderStorage { get; }

    public JsonSerializerSettings NewtonSoftDefaultSettings =>
        new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new KnownStorageAssembliesBinder(),
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
