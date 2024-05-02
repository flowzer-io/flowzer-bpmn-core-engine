using BPMN.Common;

namespace BPMN.Gateways;

public record ExclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; set; }
}