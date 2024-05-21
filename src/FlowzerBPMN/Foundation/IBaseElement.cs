namespace BPMN.Foundation;

public interface IBaseElement
{
    public string Id { get; init; }
    public FlowzerList<Documentation>? Documentations { get; init; }
    public FlowzerList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
}