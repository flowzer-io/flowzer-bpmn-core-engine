using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Artifacts;

public record CategoryValue : BaseElement
{
    public required string Value { get; init; }
    public ImmutableList<FlowElement> CategorizedFlowElements { get; init; } = [];
}