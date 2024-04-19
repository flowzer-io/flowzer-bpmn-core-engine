using System.ComponentModel.DataAnnotations;

namespace BPMN.Foundation;

public class ExtensionAttributeDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public string Type { get; set; } = "";
    public bool IsReference { get; set; }
}