using BPMN.Common;

namespace BPMN.Gateways;

public class EventBasedGateway : Gateway
{
    public EventBasedGatewayType EventBasedType { get; set; }
    public bool Instantiate { get; set; }
}