using BPMN.Foundation;

namespace BPMN.Common;

public record Message : RootElement
{
    public required string Name { get; init; }
    public ItemDefinition? ItemRef { get; init; }
}