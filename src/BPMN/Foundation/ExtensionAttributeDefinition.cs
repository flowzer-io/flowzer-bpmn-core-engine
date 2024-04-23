namespace BPMN.Foundation;

public record ExtensionAttributeDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool IsReference { get; init; }
}