namespace BPMN.Foundation;

public interface IBaseElement
{
    public string Id { get; init; }
    public List<Documentation> Documentations { get; init; }
    public List<ExtensionDefinition> ExtensionDefinitions { get; init; }
}