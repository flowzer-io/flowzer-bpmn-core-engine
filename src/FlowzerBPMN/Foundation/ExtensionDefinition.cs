namespace BPMN.Foundation;

public record ExtensionDefinition
{
    public required string Name { get; init; }
    public required FlowzerList<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; init; }
}