using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public record BoundaryEvent : CatchEvent
{
    public required bool CancelActivity { get; init; }
    public required Activity AttachedToRef { get; init; }
}