using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Activities;

public record ResourceRole : BaseElement
{
    public required string Name { get; init; }
    
    public Process.Process? Process { get; init; }
    public Resource? ResourceRef { get; init; }
    public List<ResourceParameterBinding> ResourceParameterBindings { get; init; } = [];
    public ResourceAssignmentExpression? ResourceAssignmentExpression { get; init; }
}