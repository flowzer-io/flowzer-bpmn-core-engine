using BPMN.Activities;

namespace BPMN.Events;

public record BoundaryEvent : CatchEvent
{
    public required bool CancelActivity { get; init; }
    public required Activity AttachedToRef { get; init; }
}