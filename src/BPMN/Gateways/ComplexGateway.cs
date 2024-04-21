using BPMN.Common;

namespace BPMN.Gateways;

public class ComplexGateway : Gateway
{
    public Expression? ActivationCondition { get; set; }
    public SequenceFlow? Default { get; set; }
}