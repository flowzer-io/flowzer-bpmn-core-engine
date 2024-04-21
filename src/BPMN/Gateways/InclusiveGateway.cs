using BPMN.Common;

namespace BPMN.Gateways;

public class InclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
}