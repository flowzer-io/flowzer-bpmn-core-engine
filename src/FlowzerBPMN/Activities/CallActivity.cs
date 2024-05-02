using BPMN.Common;

namespace BPMN.Activities;

public record CallActivity : Activity
{
    public ICallableElement? CalledElementRef { get; init; }
}