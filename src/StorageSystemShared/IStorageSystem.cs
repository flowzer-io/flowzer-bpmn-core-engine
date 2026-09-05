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
    
    IMessageSubscriptionStorage SubscriptionStorage { get; }
    
    IInstanceStorage InstanceStorage { get; }
    
    IFormStorage FormStorage { get; }

    /// <summary>Auftraege fuer externe Worker und deren Webhook-Anmeldungen.</summary>
    IServiceTaskStorage ServiceTaskStorage { get; }
}