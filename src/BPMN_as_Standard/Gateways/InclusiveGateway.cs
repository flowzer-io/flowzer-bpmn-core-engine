using BPMN.Common;

namespace BPMN.Gateways;

public record InclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; init; }
}