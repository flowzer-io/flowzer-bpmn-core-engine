using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Process;

public record Process : IFlowElementContainer, ICallableElement
{
    public required string Id { get; init; }
    public List<Documentation> Documentations { get; init; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; init; } = [];
    public List<FlowElement> FlowElements { get; init; } = [];
    
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
    
    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public List<InputOutputBinding> IoBindings { get; init; } = [];
    public List<Interface> SupportedInterfaceRefs { get; init; } = [];

    public string? FlowzerProcessHash { get; init; }
}