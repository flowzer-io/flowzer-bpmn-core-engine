namespace BPMN.Activities;

public record CallActivity : Activity, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public ICallableElement? CalledElementRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
    
    public required string FlowzerCalledElementProcessId { get; init; }
    public bool FlowzerPropagateAllChildVariables { get; init; }
    public bool FlowzerPropagateAllParentVariables { get; init; }
}