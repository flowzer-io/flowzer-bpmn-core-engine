using BPMN.Foundation;

namespace BPMN.Common;

public class ResourceParameter : BaseElement
{
    public required string Name { get; set; }
    public bool IsRequired { get; set; }
    
    public ItemDefinition? Type { get; set; }
}