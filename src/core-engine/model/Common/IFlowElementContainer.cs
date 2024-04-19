using Foundation;

namespace Common;

public interface IFlowElementContainer : IBaseElement
{
    public List<FlowElement> FlowElements { get; set; }
}