using Common;

namespace Activities;

public class CallActivity(string name, IFlowElementContainer container) : Activity(name, container)
{
    public CallableElement? CalledElementRef { get; set; }
}