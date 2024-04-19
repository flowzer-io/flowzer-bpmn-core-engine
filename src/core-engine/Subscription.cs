namespace core_engine;

public class Subscription
{
    /// <summary>
    /// Die ID des Service, die für dieses Event zuständig sein soll. z.B. Chron/Webhook etc....
    /// </summary>
    public required string ServiceId { get; set; }
    
    /// <summary>
    /// Die BPMN-Node an die Daten bei einem Event des Services übergeben werden sollen
    /// </summary>
    public required string BpmnNodeId { get; set; }
    
    /// <summary>
    /// Die ID der Instance des BPMN-Flows, an die die Daten bei einem Event übergeben werden soll.
    /// Null, wenn eine neue Instanz gestartet werden soll
    /// </summary>
    public required Guid? InstanceId { get; set; }
}