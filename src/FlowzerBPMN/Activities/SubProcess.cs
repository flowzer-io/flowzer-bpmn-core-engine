using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record SubProcess : Activity, IFlowElementContainer, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public bool TriggeredByEvent { get; init; }
    
    public required FlowzerList<FlowElement> FlowElements { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}