using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract class Event(string name, IFlowElementContainer container) : FlowNode(name, container)
{
    public List<Escalation> Escalations { get; set; } = [];
    public List<Property> Properties { get; set; } = [];
}