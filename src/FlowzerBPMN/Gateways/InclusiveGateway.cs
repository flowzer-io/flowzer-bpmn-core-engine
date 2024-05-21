namespace BPMN.Gateways;

public record InclusiveGateway : Gateway, IHasDefault
{
    public string? DefaultId { get; init; }
}