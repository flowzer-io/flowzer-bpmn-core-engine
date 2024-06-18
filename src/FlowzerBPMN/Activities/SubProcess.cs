namespace BPMN.Activities;

public record SubProcess : Activity, IFlowElementContainer, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public bool TriggeredByEvent { get; init; }
    
    [DoNotTranslate]
    public required FlowzerList<FlowElement> FlowElements { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}