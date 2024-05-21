namespace BPMN.Artifacts;

public record CategoryValue : BaseElement
{
    public required string Value { get; init; }
    public FlowzerList<FlowElement> CategorizedFlowElements { get; init; } = [];
}