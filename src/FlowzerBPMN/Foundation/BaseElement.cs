namespace BPMN.Foundation;

public abstract record BaseElement : IBaseElement
{
    public required string Id { get; init; } = "";
    public ImmutableList<Documentation>? Documentations { get; init; }
    public ImmutableList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
}