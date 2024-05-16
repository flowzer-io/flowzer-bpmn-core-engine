namespace BPMN.Gateways;

public record EventBasedGateway : Gateway
{
    public EventBasedGatewayType EventBasedType { get; init; }
    public bool Instantiate { get; init; }
}