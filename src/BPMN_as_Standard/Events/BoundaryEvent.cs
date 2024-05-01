using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public record BoundaryEvent : CatchEvent
{
    public required bool CancelActivity { get; init; }
    public required Activity AttachedToRef { get; init; }
}