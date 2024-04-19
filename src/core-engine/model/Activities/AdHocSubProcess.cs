using Common;

namespace Activities;

public class AdHocSubProcess(Expression completionCondition) : SubProcess
{
    public bool CancelRemainingInstances { get; set; }
    public AdHocOrdering Ordering { get; set; }
    
    public Expression CompletionCondition { get; set; } = completionCondition;
}