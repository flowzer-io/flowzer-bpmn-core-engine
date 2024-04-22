using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract class Event : Common.CatchEvent
{
    public List<Escalation> Escalations { get; set; } = [];
    public List<Property> Properties { get; set; } = [];
}