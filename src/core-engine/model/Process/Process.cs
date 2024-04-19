using System.ComponentModel.DataAnnotations;
using Activities;
using Common;
using Data;
using Foundation;
using HumanInteraction;

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
    public IBaseElement? PartitionElementRef { get; set; }
    public IBaseElement? PartitionElement { get; set; }
}

public abstract class GlobalTask(string name) : CallableElement(name)
{
    public List<ResourceRole> Resources { get; set; } = [];
}

public class GlobalBusinessRuleTask(string name, string implementation) : GlobalTask(name)
{
    public string Implementation { get; set; } = implementation;
}

public class GlobalUserTask(string name, string implementation) : GlobalTask(name)
{
    public string Implementation { get; set; } = implementation;

    public List<Rendering> Renderings { get; set; } = [];
}

public class GlobalManualTask(string name) : GlobalTask(name);

public class GlobalScriptTask(string name, string script) : GlobalTask(name)
{
    public string? ScriptLanguage { get; set; }
    public string Script { get; set; } = script;
}
 