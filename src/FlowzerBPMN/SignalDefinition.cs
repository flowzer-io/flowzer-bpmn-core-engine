using BPMN.Foundation;

namespace FlowzerBPMN;

public record SignalDefinition : BaseElement
{
    public required string Name { get; init; }
}