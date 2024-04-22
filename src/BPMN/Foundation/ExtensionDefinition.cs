namespace BPMN.Foundation;

public record ExtensionDefinition
{
    public required string Name { get; init; }
    public required List<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; init; }
}