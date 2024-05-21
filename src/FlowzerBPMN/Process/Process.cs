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
    public FlowzerList<Documentation>? Documentations { get; init; }
    public FlowzerList<ExtensionDefinition>? ExtensionDefinitions { get; init; }
    public FlowzerList<FlowElement> FlowElements { get; init; } = [];

    // public List<FlowNode> StartFlowNodes =>
    //     FlowElements
    //         .Where(x =>
    //             x is StartEvent ||
    //             x.GetType().IsSubclassOf(typeof(Activity)))
    //         .Where(x => FlowElements.OfType<SequenceFlow>().All(f => f.TargetRef != x))
    //         .Cast<FlowNode>().ToList();


    public ProcessType ProcessType { get; init; }
    public bool IsExecutable { get; init; }
    public bool IsClosed { get; init; }

    public FlowzerList<CorrelationSubscription>? CorrelationSubscriptions { get; init; }
    public FlowzerList<ResourceRole>? Resources { get; init; }
    public Process? Supports { get; init; }
    public LaneSet? LaneSet { get; init; }
    public FlowzerList<Property>? Properties { get; init; }
    public Monitoring? Monitoring { get; init; }
    public Auditing? Auditing { get; init; }

    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public FlowzerList<InputOutputBinding>? IoBindings { get; init; }
    public FlowzerList<Interface>? SupportedInterfaceRefs { get; init; }

    public string? FlowzerProcessHash { get; init; }
    public required string DefinitionsId { get; init; }
}