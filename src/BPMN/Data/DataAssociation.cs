using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Data;

public class DataAssociation(ItemAwareElement targetRef) : BaseElement
{
    public List<Assignment> Assignments { get; set; } = [];
    public FormalExpression? Transformation { get; set; }
    public ItemAwareElement TargetRef { get; set; } = targetRef;
    public ItemAwareElement? SourceRef { get; set; }
}