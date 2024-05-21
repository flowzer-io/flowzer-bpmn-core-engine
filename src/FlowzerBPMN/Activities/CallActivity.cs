using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record CallActivity : Activity, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public ICallableElement? CalledElementRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}