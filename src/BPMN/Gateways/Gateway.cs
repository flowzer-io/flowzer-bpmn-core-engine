using BPMN.Common;

namespace BPMN.Gateways;

public class Gateway : FlowNode
{
    public GatewayDirection GatewayDirection { get; set; }
}