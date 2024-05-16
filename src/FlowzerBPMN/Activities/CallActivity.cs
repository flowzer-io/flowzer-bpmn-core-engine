using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record CallActivity : Activity, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public ICallableElement? CalledElementRef { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}