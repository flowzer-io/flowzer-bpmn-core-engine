using BPMN.Common;

namespace BPMN.Gateways;

public record ComplexGateway : Gateway
{
    public Expression? ActivationCondition { get; init; }
    public SequenceFlow? Default { get; init; }
}