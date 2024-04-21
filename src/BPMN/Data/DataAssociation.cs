using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Data;

public class DataAssociation : BaseElement
{
    public List<Assignment> Assignments { get; set; } = [];
    public FormalExpression? Transformation { get; set; }
    public required ItemAwareElement TargetRef { get; set; }
    public ItemAwareElement? SourceRef { get; set; }
}