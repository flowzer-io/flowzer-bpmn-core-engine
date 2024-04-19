using Common;

namespace Activities;

public class AdHocSubProcess(string name, IFlowElementContainer container, Expression completionCondition) : SubProcess(name, container)
{
    public bool CancelRemainingInstances { get; set; }
    public AdHocOrdering Ordering { get; set; }
    
    public Expression CompletionCondition { get; set; } = completionCondition;
}