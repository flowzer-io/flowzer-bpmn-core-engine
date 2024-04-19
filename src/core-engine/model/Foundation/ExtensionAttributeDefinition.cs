using System.ComponentModel.DataAnnotations;

namespace Foundation;

public class ExtensionAttributeDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public string Type { get; set; } = "";
    public bool IsReference { get; set; }
}