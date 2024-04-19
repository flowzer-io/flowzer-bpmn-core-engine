using Activities;
using Data;

namespace Events;

public class BoundaryEvent(OutputSet outputSet, Activity attachedToRef) : CatchEvent(outputSet)
{
    public bool CancelActivity { get; set; }
    public Activity AttachedToRef { get; set; } = attachedToRef;
}