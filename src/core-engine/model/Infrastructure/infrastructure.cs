using System.ComponentModel.DataAnnotations;
using Foundation;
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace infrastructure;

public class Definitions : BaseElement
{
    [Required] public string Name { get; set; } = "";
    [Required] public string TargetNamespace { get; set; } = "";
    public string? ExpressionLanguage { get; set; }
    public string? TypeLanguage { get; set; }
    public string? Exporter { get; set; }
    public string? ExporterVersion { get; set; }
    
    public List<Extension> Extensions { get; set; } = [];
    public List<RootElement> RootElements { get; set; } = [];
}
