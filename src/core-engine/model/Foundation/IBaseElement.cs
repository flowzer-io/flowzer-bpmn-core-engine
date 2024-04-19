using System.ComponentModel.DataAnnotations;

namespace Foundation;

public interface IBaseElement
{
    [Required] public string Id { get; set; }
    public List<Documentation> Documentations { get; set; }
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; }
}