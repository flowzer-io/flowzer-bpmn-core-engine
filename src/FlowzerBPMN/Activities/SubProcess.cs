using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record SubProcess : Activity, IFlowElementContainer, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public bool TriggeredByEvent { get; init; }
    
    public required ImmutableList<FlowElement> FlowElements { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}