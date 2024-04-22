using BPMN.Common;

namespace BPMN.Gateways;

public class Gateway : CatchEvent
{
    public GatewayDirection GatewayDirection { get; set; }
}