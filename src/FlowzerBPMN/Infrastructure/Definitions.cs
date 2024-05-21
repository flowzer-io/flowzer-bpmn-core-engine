namespace BPMN.Infrastructure;

public record Definitions : BaseElement
{
    //[Required] public string Name { get; init; } = "";
    //[Required] public string TargetNamespace { get; init; } = "";
    public string? ExpressionLanguage { get; init; }
    public string? TypeLanguage { get; init; }
    public string? Exporter { get; init; }
    public string? ExporterVersion { get; init; }
    
    public FlowzerList<Extension> Extensions { get; init; } = [];
    public FlowzerList<IRootElement> RootElements { get; init; } = [];
    public FlowzerList<FlowNode> FlowNodes { get; init; } = [];
    
    public string FlowzerFileHash { get; init; } = "";
}
