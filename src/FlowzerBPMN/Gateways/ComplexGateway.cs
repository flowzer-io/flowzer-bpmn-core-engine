namespace BPMN.Gateways;

public record ComplexGateway : Gateway, IHasDefault
{
    public Expression? ActivationCondition { get; init; }
    public string? DefaultId { get; init; }
}