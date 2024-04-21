using BPMN.Common;

namespace BPMN.Gateways;

public class ExclusiveGateway : Gateway
{
    public SequenceFlow? Default { get; set; }
}