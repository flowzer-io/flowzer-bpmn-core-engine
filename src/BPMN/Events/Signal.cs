using BPMN.Common;

namespace BPMN.Events;

public class Signal
{
    public string Name { get; set; } = "";
    public ItemDefinition? StructureRef { get; set; }
}