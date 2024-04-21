using System.ComponentModel.DataAnnotations;

namespace BPMN.Foundation;

public abstract class BaseElement : IBaseElement
{
    public required string Id { get; set; } = "";
    public List<Documentation> Documentations { get; set; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; } = [];
}