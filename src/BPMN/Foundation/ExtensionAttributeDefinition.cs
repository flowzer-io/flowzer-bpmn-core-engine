namespace BPMN.Foundation;

public class ExtensionAttributeDefinition
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public bool IsReference { get; set; }
}