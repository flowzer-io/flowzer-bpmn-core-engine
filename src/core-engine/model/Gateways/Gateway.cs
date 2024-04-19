using Common;

namespace Gateways;

public class Gateway : FlowNode
{
    public GatewayDirection GatewayDirection { get; set; }
}

public class ExclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
}

public class InclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
}

public class ParallelGateway : Gateway;

public class ComplexGateway : Gateway
{
    public Expression? ActivationCondition { get; set; }
    public SequenceFlow? Default { get; set; }
}

public class EventBasedGateway : Gateway
{
    public EventBasedGatewayType EventBasedType { get; set; }
    public bool Instantiate { get; set; }
}

public enum EventBasedGatewayType
{
    Exclusive,
    Parallel
}

public enum GatewayDirection
{ 
    Unspecified,
    Converging,
    Diverging,
    Mixed
}