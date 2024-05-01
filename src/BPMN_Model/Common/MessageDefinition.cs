using BPMN_Model.Foundation;

namespace BPMN_Model.Common;

public record MessageDefinition : BaseElement
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
}