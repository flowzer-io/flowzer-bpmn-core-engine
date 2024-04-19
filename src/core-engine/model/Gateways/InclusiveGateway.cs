using Common;

namespace Gateways;

public class InclusiveGateway(string name, IFlowElementContainer container) : Gateway(name, container)
{
    public SequenceFlow? Default { get; set; }
}