using System.ComponentModel.DataAnnotations;

namespace BPMN.Foundation;

public class ExtensionDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public List<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; set; } = [];
}