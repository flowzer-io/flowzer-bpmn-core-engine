using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract record Event : FlowNode
{
    public FlowzerList<Escalation>? Escalations { get; init; }
    public FlowzerList<Property>? Properties { get; init; }
}