using Common;

namespace Gateways;

public class EventBasedGateway(string name, IFlowElementContainer container) : Gateway(name, container)
{
    public EventBasedGatewayType EventBasedType { get; set; }
    public bool Instantiate { get; set; }
}