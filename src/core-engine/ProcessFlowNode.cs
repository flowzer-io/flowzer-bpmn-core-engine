using BPMN.Activities;
using BPMN.Common;

namespace core_engine;


public class ProcessFlowNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required FlowNode FlowNode { get; set; }
    public FlowNode? ComingFrom { get; set; }
    public FlowNodeState State { get; set; } = FlowNodeState.Ready;
    
    /// <summary>
    /// Alle Input Variablen, die dem FlowNode zur Verfügung gestellt werden.
    /// </summary>
    public required Dictionary<string, object> InputVariables { get; set; }
    
    /// <summary>
    /// Alle Output Variablen, die der FlowNode zurückgibt.
    /// </summary>
    public required Dictionary<string, object> OutputVariables { get; set; }
    public required Token CallingToken { get; set; }
    public Dictionary<DateTime, FlowNodeState> TimeLog { get; init; } = [];
}