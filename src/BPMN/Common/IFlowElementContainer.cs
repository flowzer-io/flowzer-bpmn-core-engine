using BPMN.Foundation;

namespace BPMN.Common;

public interface IFlowElementContainer : IBaseElement
{
    public List<FlowElement> FlowElements { get; set; }
}