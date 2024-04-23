using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Activities;

public abstract record ResourceParameterBinding : BaseElement
{
    public required ResourceParameter ParameterRef { get; init; }
    public Expression? Expression { get; init; }
}