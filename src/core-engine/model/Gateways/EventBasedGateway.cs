namespace Gateways;

public class EventBasedGateway : Gateway
{
    public EventBasedGatewayType EventBasedType { get; set; }
    public bool Instantiate { get; set; }
}