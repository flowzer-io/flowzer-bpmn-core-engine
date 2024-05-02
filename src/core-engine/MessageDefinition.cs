using BPMN.Foundation;

namespace FlowzerBPMN;

public record MessageDefinition : BaseElement
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
}