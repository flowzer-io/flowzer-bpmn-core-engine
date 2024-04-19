using System.ComponentModel.DataAnnotations;
using Foundation;

namespace Common;

public class ResourceParameter : BaseElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsRequired { get; set; }
    
    public ItemDefinition? Type { get; set; }
}