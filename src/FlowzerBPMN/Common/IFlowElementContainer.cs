using BPMN.Foundation;

namespace BPMN.Common;

public interface IFlowElementContainer : IBaseElement
{
    public ImmutableList<FlowElement> FlowElements { get; init; }
}