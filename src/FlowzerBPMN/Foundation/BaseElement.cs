namespace BPMN.Foundation;

public abstract record BaseElement : IBaseElement
{
    public required string Id { get; init; }
    public FlowzerList<Documentation>? Documentations { get; init; }
    public FlowzerList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
}