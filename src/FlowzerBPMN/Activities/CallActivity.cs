using BPMN.Common;
using BPMN.Flowzer;

namespace BPMN.Activities;

public record CallActivity : Activity, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public ICallableElement? CalledElementRef { get; init; }
    public FlowzerIoMapping? InputMapping { get; init; }
    public FlowzerIoMapping? OutputMapping { get; init; }
}