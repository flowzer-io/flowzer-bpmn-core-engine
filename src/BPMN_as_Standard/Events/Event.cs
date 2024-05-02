using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract record Event : FlowNode
{
    public List<Escalation> Escalations { get; init; } = [];
    public List<Property> Properties { get; init; } = [];
}