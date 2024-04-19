using Activities;
using Common;
using Data;

namespace Events;

public class BoundaryEvent(string name, IFlowElementContainer container, OutputSet outputSet, Activity attachedToRef) : CatchEvent(name, container, outputSet)
{
    public bool CancelActivity { get; set; }
    public Activity AttachedToRef { get; set; } = attachedToRef;
}