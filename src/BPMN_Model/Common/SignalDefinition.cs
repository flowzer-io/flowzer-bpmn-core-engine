using BPMN_Model.Foundation;

namespace BPMN_Model.Common;

public record SignalDefinition : BaseElement
{
    public required string Name { get; init; }
}