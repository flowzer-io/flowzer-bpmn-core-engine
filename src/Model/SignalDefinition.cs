using BPMN.Foundation;

namespace Model;

public record SignalDefinition : BaseElement
{
    public required string Name { get; init; }
}