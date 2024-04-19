using BPMN.Common;

namespace BPMN.Events;

public class Escalation
{
    public string EscalationCode { get; set; } = "";
    public string Name { get; set; } = "";
    
    public ItemDefinition? StructureRef { get; set; }
}