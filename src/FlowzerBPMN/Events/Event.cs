using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract record Event : FlowNode
{
    public ImmutableList<Escalation>? Escalations { get; init; }
    public ImmutableList<Property>? Properties { get; init; }
}