using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Data;

public record DataAssociation : BaseElement
{
    public List<Assignment> Assignments { get; init; } = [];
    public FormalExpression? Transformation { get; init; }
    public required ItemAwareElement TargetRef { get; init; }
    public ItemAwareElement? SourceRef { get; init; }
}