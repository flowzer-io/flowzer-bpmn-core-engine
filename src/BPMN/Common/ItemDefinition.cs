using BPMN.Foundation;

namespace BPMN.Common;

public record ItemDefinition : RootElement
{
    public ItemKind ItemKind { get; init; }
    public object? StructureRef { get; init; }
    public bool IsCollection { get; init; }
}