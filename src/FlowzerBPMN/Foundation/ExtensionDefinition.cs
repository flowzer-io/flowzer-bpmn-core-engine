namespace BPMN.Foundation;

public record ExtensionDefinition
{
    public required string Name { get; init; }
    public required ImmutableList<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; init; }
}