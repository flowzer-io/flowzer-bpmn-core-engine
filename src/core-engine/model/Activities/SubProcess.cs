using Common;

namespace Activities;

public class SubProcess : Activity, IFlowElementContainer
{
    public bool TriggeredByEvent { get; set; }
    
    public List<FlowElement> FlowElements { get; set; } = [];
}