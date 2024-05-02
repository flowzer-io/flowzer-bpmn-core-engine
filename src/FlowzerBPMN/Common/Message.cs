using BPMN.Foundation;

namespace BPMN.Common;

public record Message : IRootElement
{
    public required string Name { get; init; }
    public ItemDefinition? ItemRef { get; init; }
}