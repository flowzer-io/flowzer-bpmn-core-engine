namespace BPMN.Foundation;

public interface IBaseElement
{
    public string Id { get; set; }
    public List<Documentation> Documentations { get; set; }
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; }
}