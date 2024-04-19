using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Foundation;

namespace BPMN.Process;

public class Process(string id, string name) : CallableElement(name), IFlowElementContainer
{
    public ProcessType ProcessType { get; set; }
    public bool IsExecutable { get; set; }
    public bool IsClosed { get; set; }
    
    public List<CorrelationSubscription> CorrelationSubscriptions { get; set; } = [];
    public List<ResourceRole> Resources { get; set; } = [];
    public Process? Supports { get; set; }
    public LaneSet? LaneSet { get; set; }
    public List<Property> Properties { get; set; } = [];
    public Monitoring? Monitoring { get; set; }
    public Auditing? Auditing { get; set; }

    public string Id { get; set; } = id;
    public List<Documentation> Documentations { get; set; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; } = [];
    public List<FlowElement> FlowElements { get; set; } = [];
}