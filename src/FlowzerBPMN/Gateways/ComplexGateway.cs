using BPMN.Activities;
using BPMN.Common;

namespace BPMN.Gateways;

public record ComplexGateway : Gateway, IHasDefault
{
    public Expression? ActivationCondition { get; init; }
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; init; }
}