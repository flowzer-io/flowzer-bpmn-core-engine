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
    
    public ImmutableList<Extension> Extensions { get; init; } = [];
    public ImmutableList<IRootElement> RootElements { get; init; } = [];
    public ImmutableList<FlowNode> FlowNodes { get; init; } = [];
    
    public string FlowzerFileHash { get; init; } = "";
}
