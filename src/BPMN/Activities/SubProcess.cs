using BPMN.Common;

namespace BPMN.Activities;

public class SubProcess(string name, IFlowElementContainer container) : Activity(name, container), IFlowElementContainer
{
    public bool TriggeredByEvent { get; set; }
    
    public List<FlowElement> FlowElements { get; set; } = [];
}