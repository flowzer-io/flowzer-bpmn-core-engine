using System.ComponentModel.DataAnnotations;
using Activities;
using Common;
using Data;
using Foundation;

namespace Process;

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

public enum ProcessType
{
    None, Public, Private
}

public abstract class Performer : ResourceRole;

public class Auditing : BaseElement;
public class Monitoring : BaseElement;

public class LaneSet
{
    [Required] public string Name { get; set; } = "";
    
    public List<Lane> Lanes { get; set; } = [];
    public Lane? ParentLane { get; set; }
}

public class Lane(LaneSet laneSet) : BaseElement
{
    [Required] public string Name { get; set; } = "";
    
    public LaneSet LaneSet { get; set; } = laneSet;
    public LaneSet? ChildLaneSet { get; set; }
    public List<FlowNode> FlowNodeRefs { get; set; } = [];
}