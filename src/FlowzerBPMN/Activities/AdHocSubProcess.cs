namespace BPMN.Activities;

public record AdHocSubProcess : SubProcess
{
    public bool CancelRemainingInstances { get; init; }
    public AdHocOrdering Ordering { get; init; }
    
    public required Expression CompletionCondition { get; init; }
}