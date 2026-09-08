namespace StorageSystem;

public interface IStorageSystem
{
    /// <summary>
    /// MetaDefinition
    ///  - Definition
    ///    + Process
    ///    + Process
    ///  + Definition
    /// </summary>
    IDefinitionStorage DefinitionStorage { get; }

    /// <summary>Ordner des Workflow-Katalogs samt der Zuweisungen, die an ihnen haengen.</summary>
    IFolderStorage FolderStorage { get; }
    
    IMessageSubscriptionStorage SubscriptionStorage { get; }
    
    IInstanceStorage InstanceStorage { get; }
    
    IFormStorage FormStorage { get; }

    /// <summary>Auftraege fuer externe Worker und deren Webhook-Anmeldungen.</summary>
    IServiceTaskStorage ServiceTaskStorage { get; }

    /// <summary>Persistente Wiederholungsverträge für direkte HTTP-Mutationen.</summary>
    IIdempotencyStorage IdempotencyStorage => UnsupportedIdempotencyStorage.Instance;
}