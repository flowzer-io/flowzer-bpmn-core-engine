using Common;

namespace Gateways;

public class ExclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
}