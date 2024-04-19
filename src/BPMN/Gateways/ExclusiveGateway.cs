using BPMN.Common;

namespace BPMN.Gateways;

public class ExclusiveGateway(string name, IFlowElementContainer container) : Gateway(name, container)
{
    public SequenceFlow? Default { get; set; }
}