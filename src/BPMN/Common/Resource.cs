using BPMN.Foundation;

namespace BPMN.Common;

public class Resource : RootElement
{
    public required string Name { get; set; }
    
    public List<ResourceParameter> ResourceParameters { get; set; } = [];
}