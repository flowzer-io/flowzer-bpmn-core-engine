using BPMN.Common;

namespace BPMN.Gateways;

public class Gateway(string name, IFlowElementContainer container) : FlowNode(name, container)
{
    public GatewayDirection GatewayDirection { get; set; }
}