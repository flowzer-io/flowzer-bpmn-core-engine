using BPMN.Common;

namespace BPMN.Activities;

public record SubProcess : Activity, IFlowElementContainer
{
    public bool TriggeredByEvent { get; init; }
    
    public List<FlowElement> FlowElements { get; init; } = [];
}