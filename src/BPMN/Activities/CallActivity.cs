using BPMN.Common;

namespace BPMN.Activities;

public class CallActivity : Activity
{
    public CallableElement? CalledElementRef { get; set; }
}