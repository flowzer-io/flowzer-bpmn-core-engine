using System.ComponentModel.DataAnnotations;

namespace Foundation;

public class ExtensionDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public List<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; set; } = [];
}