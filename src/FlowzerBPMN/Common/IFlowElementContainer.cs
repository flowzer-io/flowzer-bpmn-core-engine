using BPMN.Foundation;

namespace BPMN.Common;

public interface IFlowElementContainer : IBaseElement
{
    public FlowzerList<FlowElement> FlowElements { get; init; }
}