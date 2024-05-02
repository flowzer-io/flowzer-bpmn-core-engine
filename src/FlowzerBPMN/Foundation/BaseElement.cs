using System.ComponentModel.DataAnnotations;

namespace BPMN.Foundation;

public abstract record BaseElement : IBaseElement
{
    public required string Id { get; init; } = "";
    public List<Documentation> Documentations { get; init; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; init; } = [];
}