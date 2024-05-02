using BPMN.Common;

namespace BPMN.Events;

public record Signal
{
    public string Name { get; init; } = "";
    public ItemDefinition? StructureRef { get; init; }
}