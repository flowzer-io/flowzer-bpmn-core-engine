using BPMN.Common;

namespace BPMN.Activities;

public class AdHocSubProcess : SubProcess
{
    public bool CancelRemainingInstances { get; set; }
    public AdHocOrdering Ordering { get; set; }
    
    public required Expression CompletionCondition { get; set; }
}