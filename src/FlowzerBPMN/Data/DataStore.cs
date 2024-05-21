namespace BPMN.Data;

public record DataStore : ItemAwareElement, IRootElement
{
    public required string Name { get; init; }
    public bool IsUnlimited { get; init; }
    public int? Capacity { get; init; }
}