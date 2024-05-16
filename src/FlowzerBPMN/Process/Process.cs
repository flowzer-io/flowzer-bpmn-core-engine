using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Events;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Process;

public record Process : IFlowElementContainer, ICallableElement
{
    public required string Id { get; init; }
    public ImmutableList<Documentation>? Documentations { get; init; }
    public ImmutableList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
    public ImmutableList<FlowElement> FlowElements { get; init; } = [];

    public List<FlowNode> StartFlowNodes =>
        FlowElements
            .Where(x =>
                x is StartEvent ||
                x.GetType().IsSubclassOf(typeof(Activity)))
            .Where(x => FlowElements.OfType<SequenceFlow>().All(f => f.TargetRef != x))
            .Cast<FlowNode>().ToList();


    public ProcessType ProcessType { get; init; }
    public bool IsExecutable { get; init; }
    public bool IsClosed { get; init; }

    public ImmutableList<CorrelationSubscription>? CorrelationSubscriptions { get; init; }
    public ImmutableList<ResourceRole>? Resources { get; init; }
    public Process? Supports { get; init; }
    public LaneSet? LaneSet { get; init; }
    public ImmutableList<Property>? Properties { get; init; }
    public Monitoring? Monitoring { get; init; }
    public Auditing? Auditing { get; init; }

    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public ImmutableList<InputOutputBinding> IoBindings { get; init; } = [];
    public ImmutableList<Interface> SupportedInterfaceRefs { get; init; } = [];

    public string? FlowzerProcessHash { get; init; }
}