using Common;

namespace Gateways;

public class ComplexGateway(string name, IFlowElementContainer container) : Gateway(name, container)
{
    public Expression? ActivationCondition { get; set; }
    public SequenceFlow? Default { get; set; }
}