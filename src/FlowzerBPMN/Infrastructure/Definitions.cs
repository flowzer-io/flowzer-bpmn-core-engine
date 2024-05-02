using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Infrastructure;

public record Definitions : BaseElement
{
    //[Required] public string Name { get; init; } = "";
    //[Required] public string TargetNamespace { get; init; } = "";
    public string? ExpressionLanguage { get; init; }
    public string? TypeLanguage { get; init; }
    public string? Exporter { get; init; }
    public string? ExporterVersion { get; init; }
    
    public List<Extension> Extensions { get; init; } = [];
    public List<IRootElement> RootElements { get; init; } = [];
    public List<FlowNode> FlowNodes { get; init; } = [];
}
