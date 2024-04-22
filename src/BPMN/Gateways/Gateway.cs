using BPMN.Common;

namespace BPMN.Gateways;

public record Gateway : FlowNode
{
    public GatewayDirection GatewayDirection { get; init; }
}