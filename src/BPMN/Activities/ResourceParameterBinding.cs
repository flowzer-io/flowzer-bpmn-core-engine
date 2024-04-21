using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Activities;

public abstract class ResourceParameterBinding : BaseElement
{
    public required ResourceParameter ParameterRef { get; set; }
    public Expression? Expression { get; set; }
}