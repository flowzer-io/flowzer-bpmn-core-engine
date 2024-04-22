using BPMN.Common;

namespace BPMN.Activities;

public record CallActivity : Activity
{
    public CallableElement? CalledElementRef { get; init; }
}