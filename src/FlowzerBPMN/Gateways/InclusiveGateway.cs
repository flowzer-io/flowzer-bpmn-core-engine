using BPMN.Activities;
using BPMN.Common;

namespace BPMN.Gateways;

public record InclusiveGateway : Gateway, IHasDefault
{
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; init; }
}