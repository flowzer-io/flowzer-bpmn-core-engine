using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Foundation;

namespace BPMN.Process;

public record Process : CallableElement, IFlowElementContainer
{
    public ProcessType ProcessType { get; init; }
    public bool IsExecutable { get; init; }
    public bool IsClosed { get; init; }
    
    public List<CorrelationSubscription> CorrelationSubscriptions { get; init; } = [];
    public List<ResourceRole> Resources { get; init; } = [];
    public Process? Supports { get; init; }
    public LaneSet? LaneSet { get; init; }
    public List<Property> Properties { get; init; } = [];
    public Monitoring? Monitoring { get; init; }
    public Auditing? Auditing { get; init; }

    public required string Id { get; init; }
    public List<Documentation> Documentations { get; init; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; init; } = [];
    public List<FlowElement> FlowElements { get; init; } = [];
}