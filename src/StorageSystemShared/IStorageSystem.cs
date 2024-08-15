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
}