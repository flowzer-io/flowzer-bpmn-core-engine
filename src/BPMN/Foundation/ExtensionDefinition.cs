namespace BPMN.Foundation;

public class ExtensionDefinition
{
    public required string Name { get; set; }
    public required List<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; set; }
}