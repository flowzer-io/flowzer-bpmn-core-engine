using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record CallActivity : Activity, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public ICallableElement? CalledElementRef { get; init; }
    public List<FlowzerIoMapping>? InputMappings { get; init; }
    public List<FlowzerIoMapping>? OutputMappings { get; init; }
}