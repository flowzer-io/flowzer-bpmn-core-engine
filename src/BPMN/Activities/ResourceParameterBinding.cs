using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Activities;

public abstract class ResourceParameterBinding(ResourceParameter parameterRef) : BaseElement
{
    public ResourceParameter ParameterRef { get; set; } = parameterRef;
    public Expression? Expression { get; set; }
}