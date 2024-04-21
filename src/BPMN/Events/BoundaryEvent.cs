using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public class BoundaryEvent : CatchEvent
{
    public required bool CancelActivity { get; set; }
    public required Activity AttachedToRef { get; set; }
}