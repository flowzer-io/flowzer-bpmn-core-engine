using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Activities;

public class ResourceRole : BaseElement
{
    public required string Name { get; set; }
    
    public Process.Process? Process { get; set; }
    public Resource? ResourceRef { get; set; }
    public List<ResourceParameterBinding> ResourceParameterBindings { get; set; } = [];
    public ResourceAssignmentExpression? ResourceAssignmentExpression { get; set; }
}