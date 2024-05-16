namespace BPMN.Foundation;

public interface IBaseElement
{
    public string Id { get; init; }
    public ImmutableList<Documentation>? Documentations { get; init; }
    public ImmutableList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
}