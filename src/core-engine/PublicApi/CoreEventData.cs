namespace core_engine;

/// <summary>
/// Beschreibt ein eingehendes Ereignis für den Kernvertrag.
/// </summary>
public class CoreEventData
{
    public required Guid InstanceId { get; init; }

    public required string BpmnNodeId { get; init; }

    public Guid? InteractionId { get; init; }

    public IReadOnlyDictionary<string, object?> AdditionalData { get; init; } = new Dictionary<string, object?>();
}
