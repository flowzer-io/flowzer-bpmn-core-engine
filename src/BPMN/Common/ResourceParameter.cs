using System.ComponentModel.DataAnnotations;
using BPMN.Foundation;

namespace BPMN.Common;

public class ResourceParameter : BaseElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsRequired { get; set; }
    
    public ItemDefinition? Type { get; set; }
}