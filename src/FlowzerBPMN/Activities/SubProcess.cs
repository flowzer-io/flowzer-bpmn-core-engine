using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record SubProcess : Activity, IFlowElementContainer, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public bool TriggeredByEvent { get; init; }
    
    public List<FlowElement> FlowElements { get; init; } = [];
    public FlowzerIoMapping? InputMapping { get; init; }
    public FlowzerIoMapping? OutputMapping { get; init; }
}